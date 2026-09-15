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
    AdminSettingsService adminSettingsService,
    IOptions<FrontendOptions> frontendOptions)
{
    public async Task DispatchDueAutomaticRequestsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await adminSettingsService.GetAsync(cancellationToken);
        if (!settings.AutomaticRequestSendingEnabled)
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

        if (!await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
                db, request.ProjectMembership.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
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

        if (!await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
                db, request.ProjectMembership.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
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

        // The old link's invalidation and the new link's creation are
        // persisted together, in one commit, before the email send is even
        // attempted (CBLT-316) — so a failed send can never leave the old
        // link un-invalidated while a new one already exists. Previously the
        // new link was saved (via MagicLinkService.IssueAsync's own internal
        // SaveChangesAsync) as a side effect of building the email body,
        // ahead of this method's own save of the invalidation, with the send
        // attempted in between; a thrown SmtpException left the invalidation
        // unsaved while the new link was already durable.
        var prepared = PrepareRequestEmail(request, request.ProjectMembership, poc);
        await db.SaveChangesAsync(cancellationToken);

        await SendPreparedEmailAsync(poc, request.ProjectMembership, prepared.Body, cancellationToken);

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

        if (!await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
                db, request.ProjectMembership.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
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

    // Internal (not private) so DashboardService (CBLT-243) can reuse the
    // exact same live-computed status logic for its org/practice-wide
    // aggregate view, rather than reimplementing it.
    internal static PocResponseStatus ComputePocStatus(
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

    // All of a request's per-POC magic links (and its own Sent status) are
    // created and persisted in one commit before any email send is attempted
    // (CBLT-316) — so a send failure partway through a multi-POC request can
    // never leave some POCs with a durable link while the request itself is
    // still Scheduled and others' links were never created at all. Previously
    // each POC's link was saved individually (inside SendRequestEmailAsync,
    // interleaved with that POC's own send) ahead of the request's own status
    // save at the end of the loop.
    private async Task DispatchToAllPocsAsync(FeedbackRequest request, CancellationToken cancellationToken)
    {
        var membership = request.ProjectMembership;
        var prepared = membership.Pocs
            .Select(poc => PrepareRequestEmail(request, membership, poc))
            .ToList();

        request.Status = FeedbackRequestStatus.Sent;
        await db.SaveChangesAsync(cancellationToken);

        foreach (var item in prepared)
        {
            await SendPreparedEmailAsync(item.Poc, membership, item.Body, cancellationToken);
        }
    }

    private readonly record struct PreparedEmail(Poc Poc, string Body);

    // Builds the email body and creates (but does not save) the magic link it
    // references — callers must persist the link themselves, alongside
    // whatever other state (an invalidated prior link, the request's Sent
    // status) needs to land in the same commit, before attempting to send.
    private PreparedEmail PrepareRequestEmail(FeedbackRequest request, ProjectMembership membership, Poc poc)
    {
        var baseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');
        var link = magicLinkService.CreateUnsaved(request.Id, poc.Id);
        var feedbackUrl = $"{baseUrl}/feedback/{link.Token}";
        var body = $"Hi {poc.Name},\n\n"
            + $"Please share your feedback on {membership.Person.FullName}'s work: {feedbackUrl}\n\n"
            + "This link is valid for 7 days and can only be used once.";

        return new PreparedEmail(poc, body);
    }

    private Task SendPreparedEmailAsync(Poc poc, ProjectMembership membership, string body, CancellationToken cancellationToken) =>
        emailSender.SendAsync(poc.Email, $"Feedback request: {membership.Person.FullName}", body, cancellationToken);
}
