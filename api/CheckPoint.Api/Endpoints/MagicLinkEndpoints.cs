using CheckPoint.Api.Contracts;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class MagicLinkEndpoints
{
    public static void MapMagicLinkEndpoints(this WebApplication app)
    {
        // Guest-facing, deliberately unauthenticated (spec Section 9) — no
        // sign-in, account, or password step is ever presented to a respondent,
        // so this route carries no RequireAuthorization call at all.
        var group = app.MapGroup("/magic-links");

        group.MapGet("/{token}", async (string token, MagicLinkService service) =>
        {
            var result = await service.ValidateAsync(token);
            return result.Status switch
            {
                MagicLinkValidationStatus.Valid =>
                    Results.Ok(new MagicLinkViewResponse(result.FeedbackRequestId!.Value)),
                MagicLinkValidationStatus.NotFound => Results.NotFound("This link is not valid."),
                MagicLinkValidationStatus.Expired => Results.Problem(
                    title: "This link has expired.", statusCode: StatusCodes.Status410Gone),
                MagicLinkValidationStatus.AlreadyUsed => Results.Conflict("This feedback has already been submitted."),
                _ => Results.Problem(),
            };
        });

        group.MapPost("/{token}/submission", async (
            string token, SubmitFeedbackRequest request, FeedbackSubmissionService service) =>
        {
            var result = await service.SubmitAsync(token, request);
            return result.Status switch
            {
                FeedbackSubmissionStatus.Submitted => Results.Ok(),
                FeedbackSubmissionStatus.Invalid => Results.ValidationProblem(
                    result.Errors!.ToDictionary(e => e.Key, e => new[] { e.Value })),
                FeedbackSubmissionStatus.LinkNotFound => Results.NotFound("This link is not valid."),
                FeedbackSubmissionStatus.LinkExpired => Results.Problem(
                    title: "This link has expired.", statusCode: StatusCodes.Status410Gone),
                FeedbackSubmissionStatus.LinkAlreadyUsed => Results.Conflict("This feedback has already been submitted."),
                _ => Results.Problem(),
            };
        });
    }
}
