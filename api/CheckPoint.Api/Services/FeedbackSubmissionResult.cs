namespace CheckPoint.Api.Services;

public enum FeedbackSubmissionStatus
{
    Submitted,
    Invalid,
    LinkNotFound,
    LinkExpired,
    LinkAlreadyUsed,
}

public record FeedbackSubmissionResult(FeedbackSubmissionStatus Status, IReadOnlyDictionary<string, string>? Errors = null)
{
    public static readonly FeedbackSubmissionResult Submitted = new(FeedbackSubmissionStatus.Submitted);
    public static readonly FeedbackSubmissionResult LinkNotFound = new(FeedbackSubmissionStatus.LinkNotFound);
    public static readonly FeedbackSubmissionResult LinkExpired = new(FeedbackSubmissionStatus.LinkExpired);
    public static readonly FeedbackSubmissionResult LinkAlreadyUsed = new(FeedbackSubmissionStatus.LinkAlreadyUsed);

    public static FeedbackSubmissionResult Invalid(IReadOnlyDictionary<string, string> errors) =>
        new(FeedbackSubmissionStatus.Invalid, errors);
}
