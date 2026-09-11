namespace CheckPoint.Api.Services;

public enum MagicLinkValidationStatus
{
    Valid,
    NotFound,
    Expired,
    AlreadyUsed,
}

public record MagicLinkValidationResult(MagicLinkValidationStatus Status, Guid? FeedbackRequestId)
{
    public static MagicLinkValidationResult Valid(Guid feedbackRequestId) =>
        new(MagicLinkValidationStatus.Valid, feedbackRequestId);

    public static readonly MagicLinkValidationResult NotFound = new(MagicLinkValidationStatus.NotFound, null);
    public static readonly MagicLinkValidationResult Expired = new(MagicLinkValidationStatus.Expired, null);
    public static readonly MagicLinkValidationResult AlreadyUsed = new(MagicLinkValidationStatus.AlreadyUsed, null);
}
