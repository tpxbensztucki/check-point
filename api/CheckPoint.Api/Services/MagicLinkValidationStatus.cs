namespace CheckPoint.Api.Services;

public enum MagicLinkValidationStatus
{
    Valid,
    NotFound,
    Expired,
    AlreadyUsed,

    // A manual reminder (CBLT-236) issued a fresh link for the same POC/request,
    // superseding this one — distinct from AlreadyUsed, which means a submission
    // was made through it.
    Superseded,
}

public record MagicLinkValidationResult(MagicLinkValidationStatus Status, Guid? FeedbackRequestId)
{
    public static MagicLinkValidationResult Valid(Guid feedbackRequestId) =>
        new(MagicLinkValidationStatus.Valid, feedbackRequestId);

    public static readonly MagicLinkValidationResult NotFound = new(MagicLinkValidationStatus.NotFound, null);
    public static readonly MagicLinkValidationResult Expired = new(MagicLinkValidationStatus.Expired, null);
    public static readonly MagicLinkValidationResult AlreadyUsed = new(MagicLinkValidationStatus.AlreadyUsed, null);
    public static readonly MagicLinkValidationResult Superseded = new(MagicLinkValidationStatus.Superseded, null);
}
