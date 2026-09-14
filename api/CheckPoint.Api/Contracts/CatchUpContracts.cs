using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record CatchUpResponse(
    Guid Id,
    Guid PersonId,
    Guid? FeedbackRequestId,
    CatchUpStatus Status,
    DateTimeOffset CreatedAt,
    CatchUpOutcomeType? OutcomeType,
    string? OutcomeNotes,
    DateTimeOffset? RecordedAt)
{
    public static CatchUpResponse From(CatchUp catchUp) => new(
        catchUp.Id,
        catchUp.PersonId,
        catchUp.FeedbackRequestId,
        catchUp.Status,
        catchUp.CreatedAt,
        catchUp.OutcomeType,
        catchUp.OutcomeNotes,
        catchUp.RecordedAt);
}

public record AdHocReviewResponse(CatchUpResponse CatchUp, bool AlreadyPending);

public record RecordCatchUpOutcomeRequest(CatchUpOutcomeType OutcomeType, string? Notes);
