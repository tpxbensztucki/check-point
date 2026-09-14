using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum AdHocReviewStatus
{
    Triggered,
    PersonNotFound,
    Forbidden,
}

public record AdHocReviewResult(AdHocReviewStatus Status, AdHocReviewResponse? Review = null, string? Error = null)
{
    public static AdHocReviewResult Triggered(CatchUpResponse catchUp, bool alreadyPending) =>
        new(AdHocReviewStatus.Triggered, new AdHocReviewResponse(catchUp, alreadyPending));

    public static readonly AdHocReviewResult Forbidden = new(AdHocReviewStatus.Forbidden);

    public static AdHocReviewResult PersonNotFound(string error) => new(AdHocReviewStatus.PersonNotFound, Error: error);
}
