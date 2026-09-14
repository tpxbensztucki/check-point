using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CheckPoint.Api.Services;

// Sends the POC feedback request email containing a magic link (spec Section 7).
// One MagicLink (and one email) per currently-assigned POC on the request's
// ProjectMembership, scoped to that POC and that specific request only
// (CBLT-302 made this possible). Callable two ways: DispatchDueAutomaticRequestsAsync
// is the Automatic-mode driver, polled by RequestDispatchBackgroundService;
// DispatchManuallyAsync is the authorised-user trigger used when the global mode
// is Manual (or as a forced send regardless of mode).
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

    private async Task DispatchToAllPocsAsync(FeedbackRequest request, CancellationToken cancellationToken)
    {
        var membership = request.ProjectMembership;
        var baseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');

        foreach (var poc in membership.Pocs)
        {
            var link = await magicLinkService.IssueAsync(request.Id, poc.Id, cancellationToken);
            var feedbackUrl = $"{baseUrl}/feedback/{link.Token}";
            var body = $"Hi {poc.Name},\n\n"
                + $"Please share your feedback on {membership.Person.FullName}'s work: {feedbackUrl}\n\n"
                + "This link is valid for 7 days and can only be used once.";

            await emailSender.SendAsync(
                poc.Email, $"Feedback request: {membership.Person.FullName}", body, cancellationToken);
        }

        request.Status = FeedbackRequestStatus.Sent;
        await db.SaveChangesAsync(cancellationToken);
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
