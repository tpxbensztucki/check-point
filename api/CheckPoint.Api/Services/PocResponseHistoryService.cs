using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Tracks non-response as a pattern across check-ins, not just per single
// request (spec Section 5.4, CBLT-238) — reuses the same per-(request, POC)
// status computation CBLT-237 established (submitted / still within its
// window / past expiry with no response), generalized across every request a
// POC was ever dispatched to, rather than just the one most recent request.
public class PocResponseHistoryService(CheckPointDbContext db, TimeProvider timeProvider)
{
    // Single-target gate: same Admin/LM-of-person/PL-of-person's-practice
    // scoping as PocService/RequestDispatchService.
    public async Task<PocHistoryResult> GetPocHistoryAsync(
        Guid pocId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var poc = await db.Pocs
            .Include(p => p.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(p => p.Id == pocId, cancellationToken);
        if (poc is null)
        {
            return PocHistoryResult.PocNotFound($"No Poc found with id {pocId}.");
        }

        if (!await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
                db, poc.ProjectMembership.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return PocHistoryResult.Forbidden;
        }

        var (entries, consecutiveNoResponse, totalNoResponse) =
            await ComputeHistoryAsync(poc.Id, poc.ProjectMembershipId, cancellationToken);

        return PocHistoryResult.Success(new PocResponseHistoryResponse(
            poc.Id, poc.Name, consecutiveNoResponse, totalNoResponse, entries));
    }

    // Filtered-list view (spec Section 8): Admin sees every POC on the
    // Project; a Practice Lead sees POCs whose membership belongs to a Person
    // in a Practice they lead; a Line Manager sees POCs whose membership
    // belongs to themselves or a direct report — same union-of-visible-ids
    // shape as OrgTreeService.GetOrgTreeForViewerAsync, not a single pass/fail
    // gate, since different POCs on the same Project can belong to People the
    // caller can and can't see.
    public async Task<ProjectPocPatternsResult> GetProjectPocPatternsAsync(
        Guid projectId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return ProjectPocPatternsResult.ProjectNotFound($"No Project found with id {projectId}.");
        }

        HashSet<Guid>? visiblePersonIds = null;
        if (!callerIsAdmin)
        {
            visiblePersonIds = [];

            if (callerIsPracticeLead)
            {
                var ledPracticeIds = await db.Practices
                    .Where(p => p.PracticeLeadId == callerId)
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken);

                if (ledPracticeIds.Count > 0)
                {
                    var practicePeopleIds = await db.People
                        .Where(p => ledPracticeIds.Contains(p.PracticeId))
                        .Select(p => p.Id)
                        .ToListAsync(cancellationToken);
                    visiblePersonIds.UnionWith(practicePeopleIds);
                }
            }

            if (callerIsLineManager)
            {
                visiblePersonIds.Add(callerId);
                var reportIds = await db.People
                    .Where(p => p.LineManagerId == callerId)
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken);
                visiblePersonIds.UnionWith(reportIds);
            }
        }

        var pocsQuery = db.Pocs.Where(p => p.ProjectMembership.ProjectId == projectId);
        if (visiblePersonIds is not null)
        {
            pocsQuery = pocsQuery.Where(p => visiblePersonIds.Contains(p.ProjectMembership.PersonId));
        }

        var pocs = await pocsQuery
            .Select(p => new { p.Id, p.Name, p.ProjectMembershipId, PersonName = p.ProjectMembership.Person.FullName })
            .ToListAsync(cancellationToken);

        var entries = new List<ProjectPocPatternEntry>();
        foreach (var poc in pocs)
        {
            var (_, consecutiveNoResponse, totalNoResponse) =
                await ComputeHistoryAsync(poc.Id, poc.ProjectMembershipId, cancellationToken);
            entries.Add(new ProjectPocPatternEntry(poc.Id, poc.Name, poc.PersonName, consecutiveNoResponse, totalNoResponse));
        }

        return ProjectPocPatternsResult.Success(entries);
    }

    private async Task<(IReadOnlyList<PocResponseHistoryEntry> Entries, int ConsecutiveNoResponse, int TotalNoResponse)>
        ComputeHistoryAsync(Guid pocId, Guid projectMembershipId, CancellationToken cancellationToken)
    {
        var requests = await db.FeedbackRequests
            .Where(r => r.ProjectMembershipId == projectMembershipId)
            .OrderByDescending(r => r.ScheduledFor)
            .ToListAsync(cancellationToken);
        var requestIds = requests.Select(r => r.Id).ToList();

        var submittedRequestIds = (await db.FeedbackSubmissions
            .Where(s => s.PocId == pocId && requestIds.Contains(s.FeedbackRequestId))
            .Select(s => s.FeedbackRequestId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        // A reminder invalidates the prior link, so at most one non-invalidated
        // link should exist per (request, POC); a used link's InvalidatedAt
        // stays null, so a submitted request's link is still found here too.
        var currentLinkByRequest = (await db.MagicLinks
            .Where(l => l.PocId == pocId && l.InvalidatedAt == null && requestIds.Contains(l.FeedbackRequestId))
            .ToListAsync(cancellationToken))
            .GroupBy(l => l.FeedbackRequestId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.IssuedAt).First());

        var now = timeProvider.GetUtcNow();
        var entries = new List<PocResponseHistoryEntry>();
        foreach (var request in requests)
        {
            var wasSubmitted = submittedRequestIds.Contains(request.Id);
            var hasCurrentLink = currentLinkByRequest.TryGetValue(request.Id, out var link);

            // This POC was never dispatched to for this request (e.g. added to
            // the membership after it fired) — it never involved them, so it's
            // excluded from their history entirely rather than counted as
            // anything.
            if (!wasSubmitted && !hasCurrentLink)
            {
                continue;
            }

            var status = wasSubmitted
                ? PocResponseStatus.Submitted
                : now > link!.ExpiresAt
                    ? PocResponseStatus.NoResponse
                    : PocResponseStatus.Sent;

            entries.Add(new PocResponseHistoryEntry(request.Id, request.ScheduledFor, status));
        }

        // entries are already ordered most-recent-first (requests was).
        var consecutiveNoResponse = 0;
        foreach (var entry in entries)
        {
            if (entry.Status != PocResponseStatus.NoResponse)
            {
                break;
            }

            consecutiveNoResponse++;
        }

        var totalNoResponse = entries.Count(e => e.Status == PocResponseStatus.NoResponse);
        return (entries, consecutiveNoResponse, totalNoResponse);
    }
}
