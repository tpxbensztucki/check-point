using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record CatchUpResponse(
    Guid Id,
    Guid PersonId,
    Guid? FeedbackRequestId,
    CatchUpStatus Status,
    DateTimeOffset CreatedAt);

public record AdHocReviewResponse(CatchUpResponse CatchUp, bool AlreadyPending);
