using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CheckPoint.Api.Services;

// Delivers the durable LmNotification outbox rows FeedbackSubmissionService
// queues (spec Section 6/7) — one email per submission, never batched, so an LM
// gets a separate notification for each of a request's respondents (CBLT-235).
// The email contains a link into the system, never the feedback content itself
// (spec Section 7's sensitivity requirement).
public class LmNotificationDispatchService(
    CheckPointDbContext db,
    TimeProvider timeProvider,
    IEmailSender emailSender,
    IOptions<FrontendOptions> frontendOptions)
{
    public async Task DispatchPendingNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await db.LmNotifications
            .Where(n => n.SentAt == null)
            .Include(n => n.LineManager)
            .Include(n => n.FeedbackSubmission).ThenInclude(s => s.Poc)
            .Include(n => n.FeedbackSubmission).ThenInclude(s => s.FeedbackRequest).ThenInclude(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            // No email on file yet for this Line Manager — nothing to send to,
            // not a delivery failure (same reasoning as "no Line Manager
            // assigned" in FeedbackSubmissionService). Left pending, retried
            // on the next pass once one is set.
            if (notification.LineManager.Email is null)
            {
                continue;
            }

            var submission = notification.FeedbackSubmission;
            var reviewee = submission.FeedbackRequest.ProjectMembership.Person;
            var baseUrl = frontendOptions.Value.BaseUrl.TrimEnd('/');

            // This route doesn't exist in the frontend yet — no Person-detail
            // view is built until Milestone 9. Placeholder shape for whoever
            // builds it; no magic-link-style token is needed here since an LM
            // is a standing system user, protected by ordinary [Authorize]
            // once that page exists, not a one-time guest link.
            var reviewUrl = $"{baseUrl}/people/{reviewee.Id}";
            var body = $"Hi,\n\n"
                + $"{submission.Poc.Name} has submitted feedback on {reviewee.FullName}'s work. "
                + $"Review it here: {reviewUrl}";

            await emailSender.SendAsync(
                notification.LineManager.Email, $"New feedback submitted for {reviewee.FullName}", body, cancellationToken);

            notification.SentAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
