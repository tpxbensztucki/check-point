using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Org/practice-wide aggregate views for the Admin/Practice Lead Dashboard
// (spec Section 12, Milestone 9) — a distinct enough concern from
// RequestDispatchService's dispatch mechanics and OrgTreeService's single-tree
// concern to warrant its own service, same reasoning as every prior service
// split in this project (RequestDispatchService/PocResponseHistoryService,
// CatchUpService).
public class DashboardService(CheckPointDbContext db, TimeProvider timeProvider)
{
    // "Outstanding" only makes sense for a request that has actually been
    // dispatched (Status == Sent) — a still-Scheduled request hasn't asked
    // anyone for anything yet, so it's excluded here rather than surfacing
    // every not-yet-due future check-in as if it needed a response. Within a
    // Sent request, an entry counts if its live-computed status isn't
    // Submitted — NotYetSent (a POC added after dispatch), Sent (awaiting
    // response), or NoResponse (CBLT-243's own two example statuses). Reuses
    // RequestDispatchService.ComputePocStatus, the exact same live-computed
    // logic GetPocStatusesAsync uses for a single request, rather than
    // reimplementing it for this org-wide view.
    public async Task<IReadOnlyList<OutstandingRequestEntry>> GetOutstandingRequestsAsync(
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var visiblePersonIds = await PersonAuthorizationHelpers.GetVisiblePersonIdsAsync(
            db, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken);

        if (visiblePersonIds is { Count: 0 })
        {
            return [];
        }

        var requestsQuery = db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Project)
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Pocs)
            .Where(r => r.Status == FeedbackRequestStatus.Sent && r.ProjectMembership.RemovedAt == null);

        if (visiblePersonIds is not null)
        {
            requestsQuery = requestsQuery.Where(r => visiblePersonIds.Contains(r.ProjectMembership.PersonId));
        }

        var requests = await requestsQuery.ToListAsync(cancellationToken);
        var requestIds = requests.Select(r => r.Id).ToList();

        var submittedByRequest = (await db.FeedbackSubmissions
            .Where(s => requestIds.Contains(s.FeedbackRequestId))
            .Select(s => new { s.FeedbackRequestId, s.PocId })
            .ToListAsync(cancellationToken))
            .GroupBy(s => s.FeedbackRequestId)
            .ToDictionary(g => g.Key, g => g.Select(s => s.PocId).ToHashSet());

        var currentLinksByRequest = (await db.MagicLinks
            .Where(l => requestIds.Contains(l.FeedbackRequestId) && l.InvalidatedAt == null)
            .ToListAsync(cancellationToken))
            .GroupBy(l => l.FeedbackRequestId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(l => l.PocId).ToDictionary(pg => pg.Key, pg => pg.OrderByDescending(l => l.IssuedAt).First()));

        var now = timeProvider.GetUtcNow();
        var entries = new List<OutstandingRequestEntry>();
        foreach (var request in requests)
        {
            var membership = request.ProjectMembership;
            var submittedPocIds = submittedByRequest.GetValueOrDefault(request.Id, new HashSet<Guid>());
            var currentLinkByPoc = currentLinksByRequest.GetValueOrDefault(request.Id, new Dictionary<Guid, MagicLink>());

            foreach (var poc in membership.Pocs)
            {
                var status = RequestDispatchService.ComputePocStatus(
                    request.Status, poc.Id, submittedPocIds, currentLinkByPoc, now);
                if (status == PocResponseStatus.Submitted)
                {
                    continue;
                }

                entries.Add(new OutstandingRequestEntry(
                    request.Id,
                    membership.PersonId,
                    membership.Person.FullName,
                    membership.ProjectId,
                    membership.Project.Name,
                    poc.Id,
                    poc.Name,
                    request.Stage,
                    status));
            }
        }

        return entries;
    }

    // Everyone currently Under Review with a Pending CatchUp (CBLT-244) —
    // keyed off CatchUp.Status == Pending, not Person.UnderReviewSince, so an
    // EscalateFurther outcome (which leaves UnderReviewSince set but resolves
    // that CatchUp) correctly drops the Person from this list until a fresh
    // flag or ad-hoc trigger opens a new one. Same visible-person-ids scoping
    // as GetOutstandingRequestsAsync.
    public async Task<IReadOnlyList<FlaggedPersonEntry>> GetFlaggedPeopleAsync(
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var visiblePersonIds = await PersonAuthorizationHelpers.GetVisiblePersonIdsAsync(
            db, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken);

        if (visiblePersonIds is { Count: 0 })
        {
            return [];
        }

        var catchUpsQuery = db.CatchUps
            .Include(c => c.Person)
            .Where(c => c.Status == CatchUpStatus.Pending);

        if (visiblePersonIds is not null)
        {
            catchUpsQuery = catchUpsQuery.Where(c => visiblePersonIds.Contains(c.PersonId));
        }

        var catchUps = await catchUpsQuery
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return catchUps
            .Select(c => new FlaggedPersonEntry(
                c.PersonId,
                c.Person.FullName,
                c.Id,
                c.FeedbackRequestId is null ? CatchUpTriggerSource.AdHoc : CatchUpTriggerSource.CheckIn,
                c.CreatedAt))
            .ToList();
    }
}
