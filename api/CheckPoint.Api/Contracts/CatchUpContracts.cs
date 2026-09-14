using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

// TriggerSource is computed from whether FeedbackRequestId is set — a cleaner
// client-facing signal than making every consumer re-derive it themselves
// (CBLT-242's own AC calls out "trigger source" as a field the history view
// must show).
public enum CatchUpTriggerSource
{
    CheckIn,
    AdHoc,
}

public record CatchUpResponse(
    Guid Id,
    Guid PersonId,
    Guid? FeedbackRequestId,
    CatchUpTriggerSource TriggerSource,
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
        catchUp.FeedbackRequestId is null ? CatchUpTriggerSource.AdHoc : CatchUpTriggerSource.CheckIn,
        catchUp.Status,
        catchUp.CreatedAt,
        catchUp.OutcomeType,
        catchUp.OutcomeNotes,
        catchUp.RecordedAt);
}

public record AdHocReviewResponse(CatchUpResponse CatchUp, bool AlreadyPending);

public record RecordCatchUpOutcomeRequest(CatchUpOutcomeType OutcomeType, string? Notes);

// UnderReviewSince surfaced at the top level so a currently-active review is
// trivially distinguishable from resolved historical entries (CBLT-242's own
// AC: "current Under Review status... highlighted separately").
public record PersonCatchUpHistoryResponse(
    Guid PersonId,
    DateTimeOffset? UnderReviewSince,
    IReadOnlyList<CatchUpResponse> Entries);
