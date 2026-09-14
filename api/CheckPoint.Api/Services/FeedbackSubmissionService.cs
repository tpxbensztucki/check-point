using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Handles a guest's completed feedback form (spec Section 6, 7, 9). Content
// validation happens before the magic link is touched at all, so a rejected
// submission never burns the guest's one chance to submit.
public class FeedbackSubmissionService(CheckPointDbContext db, TimeProvider timeProvider, MagicLinkService magicLinkService)
{
    public const int MaxFieldLength = 2000;

    public async Task<FeedbackSubmissionResult> SubmitAsync(
        string token, SubmitFeedbackRequest request, CancellationToken cancellationToken = default)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return FeedbackSubmissionResult.Invalid(errors);
        }

        var (link, status) = await magicLinkService.LoadAndCheckAsync(token, cancellationToken);
        if (status != MagicLinkValidationStatus.Valid)
        {
            return status switch
            {
                MagicLinkValidationStatus.Expired => FeedbackSubmissionResult.LinkExpired,
                MagicLinkValidationStatus.AlreadyUsed => FeedbackSubmissionResult.LinkAlreadyUsed,
                MagicLinkValidationStatus.Superseded => FeedbackSubmissionResult.LinkSuperseded,
                _ => FeedbackSubmissionResult.LinkNotFound,
            };
        }

        var feedbackRequest = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == link!.FeedbackRequestId, cancellationToken);
        if (feedbackRequest is null)
        {
            return FeedbackSubmissionResult.LinkNotFound;
        }

        var now = timeProvider.GetUtcNow();

        // FeedbackRequest.Status is untouched here — it tracks the request's own
        // dispatch lifecycle (Scheduled/Sent/Cancelled), not response state,
        // since a request can have several POCs each responding independently
        // (see FeedbackRequestStatus's own doc comment).
        link!.UsedAt = now;

        var submission = new FeedbackSubmission
        {
            FeedbackRequestId = feedbackRequest.Id,
            PocId = link.PocId,
            DoingWell = request.DoingWell,
            NotDoingWell = request.NotDoingWell,
            NeedsToImprove = request.NeedsToImprove,
            SubmittedAt = now,
        };
        db.FeedbackSubmissions.Add(submission);

        // No line manager assigned yet (spec allows this to be set later) means
        // there is genuinely nobody to notify, not a delivery failure — so no
        // outbox row is the correct outcome, not a dropped one.
        var lineManagerId = feedbackRequest.ProjectMembership.Person.LineManagerId;
        if (lineManagerId is not null)
        {
            db.LmNotifications.Add(new LmNotification
            {
                FeedbackSubmission = submission,
                LineManagerId = lineManagerId.Value,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return FeedbackSubmissionResult.Submitted;
    }

    private static Dictionary<string, string> Validate(SubmitFeedbackRequest request)
    {
        var errors = new Dictionary<string, string>();
        CheckField(errors, nameof(request.DoingWell), request.DoingWell);
        CheckField(errors, nameof(request.NotDoingWell), request.NotDoingWell);
        CheckField(errors, nameof(request.NeedsToImprove), request.NeedsToImprove);
        return errors;
    }

    private static void CheckField(Dictionary<string, string> errors, string fieldName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[fieldName] = "This field is required.";
        }
        else if (value.Length > MaxFieldLength)
        {
            errors[fieldName] = $"This field must be {MaxFieldLength} characters or fewer.";
        }
    }
}
