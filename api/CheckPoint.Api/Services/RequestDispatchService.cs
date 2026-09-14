using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CheckPoint.Api.Services;

// Sends the POC feedback request email containing a magic link (spec Section 7).
// One MagicLink (and one email) per currently-assigned POC on the request's
// ProjectMembership, scoped to that POC and that specific request only
// (CBLT-302 made this possible). Three entry points: DispatchDueAutomaticRequestsAsync
// is the Automatic-mode driver, polled by RequestDispatchBackgroundService;
// DispatchManuallyAsync is the authorised-user trigger used when the global mode
// is Manual (or as a forced send regardless of mode); SendReminderAsync (CBLT-236)
// resends to a single non-responding POC on an already-dispatched request;
// GetPocStatusesAsync (CBLT-237) reports each currently-assigned POC's outcome.
public class RequestDispatchService(
    CheckPointDbContext db,
    TimeProvider timeProvider,
    IEmailSender emailSender,
    MagicLinkService magicLinkService,
    IOptions<RequestDispatchOptions> dispatchOptions,
    IOptions<FrontendOptions> frontendOptions)
{
    public async Task DispatchDueAutomaticRequestsAsync(CancellationToken cancellationToken = default)
    {
        if (dispatchOptions.Value.Mode != RequestDispatchMode.Automatic)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var dueRequests = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Pocs)
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .Where(r => r.Status == FeedbackRequestStatus.Scheduled && r.ScheduledFor <= now)
            .ToListAsync(cancellationToken);

        foreach (var request in dueRequests)
        {
            var membership = request.ProjectMembership;

            // The person is no longer on this project — nothing to send.
            // ProjectService.CompleteProjectAsync already cancels requests when the
            // whole Project completes; this covers the narrower per-Person removal.
            if (membership.RemovedAt is not null)
            {
                request.Status = FeedbackRequestStatus.Cancelled;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            // No POC assigned yet — leave Scheduled and retry on the next pass
            // rather than sending nothing and marking it Sent regardless.
            if (membership.Pocs.Count == 0)
            {
                continue;
            }

            await DispatchToAllPocsAsync(request, cancellationToken);
        }
    }

    public async Task<RequestDispatchResult> DispatchManuallyAsync(
        Guid feedbackRequestId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var request = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Pocs)
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);
        if (request is null)
        {
            return RequestDispatchResult.RequestNotFound($"No FeedbackRequest found with id {feedbackRequestId}.");
        }

        if (!await IsAuthorizedAsync(
                request.ProjectMembership.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return RequestDispatchResult.Forbidden;
        }

        if (request.Status != FeedbackRequestStatus.Scheduled)
        {
            return RequestDispatchResult.NotCurrentlyScheduled(
                $"FeedbackRequest {feedbackRequestId} is {request.Status}, not Scheduled.");
        }

        if (request.ProjectMembership.RemovedAt is not null)
        {
            return RequestDispatchResult.NotCurrentlyScheduled(
                "This person is no longer an active member of the project.");
        }

        if (request.ProjectMembership.Pocs.Count == 0)
        {
            return RequestDispatchResult.NoPocsAssigned("No POC is currently assigned to send this request to.");
        }

        await DispatchToAllPocsAsync(request, cancellationToken);
        return RequestDispatchResult.Dispatched;
    }

    // Resends the request to one POC who hasn't yet responded (spec Section 5.4,
    // 7) — a plain resend, not a new FeedbackRequest: issues a fresh magic link
    // (fresh 7-day expiry) and invalidates whatever prior, still-usable link(s)
    // existed for this exact (request, POC) pair, so the old one stops working.
    // Callable repeatedly; each call supersedes the previous link.
    public async Task<ReminderResult> SendReminderAsync(
        Guid feedbackRequestId,
        Guid pocId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var request = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);
        if (request is null)
        {
            return ReminderResult.RequestNotFound($"No FeedbackRequest found with id {feedbackRequestId}.");
        }

        if (!await IsAuthorizedAsync(
                request.ProjectMembership.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return ReminderResult.Forbidden;
        }

        if (request.Status != FeedbackRequestStatus.Sent)
        {
            return ReminderResult.NotYetDispatched(
                $"FeedbackRequest {feedbackRequestId} is {request.Status}, not Sent — nothing to remind about yet.");
        }

        var poc = await db.Pocs.SingleOrDefaultAsync(
            p => p.Id == pocId && p.ProjectMembershipId == request.ProjectMembershipId, cancellationToken);
        if (poc is null)
        {
            return ReminderResult.PocNotFound($"No Poc found with id {pocId} on this request.");
        }

        var alreadySubmitted = await db.FeedbackSubmissions.AnyAsync(
            s => s.FeedbackRequestId == feedbackRequestId && s.PocId == pocId, cancellationToken);
        if (alreadySubmitted)
        {
            return ReminderResult.AlreadySubmitted("This POC has already submitted feedback for this request.");
        }

        var now = timeProvider.GetUtcNow();
        var stillUsableLinks = await db.MagicLinks
            .Where(l => l.FeedbackRequestId == feedbackRequestId && l.PocId == pocId
                && l.UsedAt == null && l.InvalidatedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var link in stillUsableLinks)
        {
            link.InvalidatedAt = now;
        }

        await SendRequestEmailAsync(request, request.ProjectMembership, poc, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return ReminderResult.Sent;
    }

    // Per-POC outcome for a request (spec Section 5.4, 9, CBLT-237) — computed
    // live from current time, submissions, and the most recent non-superseded
    // MagicLink per POC, rather than a stored flag flipped by a background job:
    // it's always correct instantly, with no risk of drifting out of sync with
    // "now" the way a periodically-run job could.
    public async Task<PocStatusResult> GetPocStatusesAsync(
        Guid feedbackRequestId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var request = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Pocs)
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);
        if (request is null)
        {
            return PocStatusResult.RequestNotFound($"No FeedbackRequest found with id {feedbackRequestId}.");
        }

        if (!await IsAuthorizedAsync(
                request.ProjectMembership.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return PocStatusResult.Forbidden;
        }

        var submittedPocIds = (await db.FeedbackSubmissions
            .Where(s => s.FeedbackRequestId == feedbackRequestId)
            .Select(s => s.PocId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        // A reminder invalidates the prior link, so at most one non-invalidated
        // link should exist per POC in practice; OrderByDescending is just a
        // defensive tie-breaker if that were ever violated.
        var currentLinkByPoc = (await db.MagicLinks
            .Where(l => l.FeedbackRequestId == feedbackRequestId && l.InvalidatedAt == null)
            .ToListAsync(cancellationToken))
            .GroupBy(l => l.PocId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.IssuedAt).First());

        var now = timeProvider.GetUtcNow();
        var entries = new List<PocResponseStatusEntry>();
        foreach (var poc in request.ProjectMembership.Pocs)
        {
            var status = ComputePocStatus(request.Status, poc.Id, submittedPocIds, currentLinkByPoc, now);
            entries.Add(new PocResponseStatusEntry(poc.Id, poc.Name, poc.Email, status));
        }

        return PocStatusResult.Success(entries);
    }

    private static PocResponseStatus ComputePocStatus(
        FeedbackRequestStatus requestStatus,
        Guid pocId,
        HashSet<Guid> submittedPocIds,
        Dictionary<Guid, MagicLink> currentLinkByPoc,
        DateTimeOffset now)
    {
        if (requestStatus == FeedbackRequestStatus.Cancelled)
        {
            return PocResponseStatus.Cancelled;
        }

        if (submittedPocIds.Contains(pocId))
        {
            return PocResponseStatus.Submitted;
        }

        if (requestStatus == FeedbackRequestStatus.Scheduled || !currentLinkByPoc.TryGetValue(pocId, out var link))
        {
            return PocResponseStatus.NotYetSent;
        }

        return now > link.ExpiresAt ? PocResponseStatus.NoResponse : PocResponseStatus.Sent;
    }

    private async Task DispatchToAllPocsAsync(FeedbackRequest request, CancellationToken cancellationToken)
    {
        var membership = request.ProjectMembership;
        foreach (var poc in membership.Pocs)
        {
            await SendRequestEmailAsync(request, membership, poc, cancellationToken);
        }

        request.Status = FeedbackRequestStatus.Sent;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendRequestEmailAsync(
        FeedbackRequest request, ProjectMembership membership, Poc poc, CancellationToken cancellationToken)
    {
        var baseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');
        var link = await magicLinkService.IssueAsync(request.Id, poc.Id, cancellationToken);
        var feedbackUrl = $"{baseUrl}/feedback/{link.Token}";
        var body = $"Hi {poc.Name},\n\n"
            + $"Please share your feedback on {membership.Person.FullName}'s work: {feedbackUrl}\n\n"
            + "This link is valid for 7 days and can only be used once.";

        await emailSender.SendAsync(
            poc.Email, $"Feedback request: {membership.Person.FullName}", body, cancellationToken);
    }

    // Same scoping as PocService.IsAuthorizedAsync (Admin, or the Line Manager /
    // Practice Lead of the Person the request is about) — spec Section 8.
    private async Task<bool> IsAuthorizedAsync(
        Person person,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken)
    {
        if (callerIsAdmin)
        {
            return true;
        }

        if (callerIsLineManager && person.LineManagerId == callerId)
        {
            return true;
        }

        if (callerIsPracticeLead &&
            await db.Practices.AnyAsync(p => p.Id == person.PracticeId && p.PracticeLeadId == callerId, cancellationToken))
        {
            return true;
        }

        return false;
    }
}
