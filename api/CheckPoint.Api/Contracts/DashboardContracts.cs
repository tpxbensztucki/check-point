using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

// One (FeedbackRequest, Poc) pair not yet resolved (CBLT-243) — "outstanding"
// means the live-computed PocResponseStatus isn't Submitted (and the request
// itself isn't Cancelled): NotYetSent, Sent (awaiting response), or
// NoResponse. Grouping/filtering "by cycle" (the ticket's own AC) is left to
// the frontend, which already has Stage on every entry — no per-stage
// backend endpoint, same "compute/derive, don't pre-slice" precedent as
// CatchUpResponse.TriggerSource.
public record OutstandingRequestEntry(
    Guid FeedbackRequestId,
    Guid PersonId,
    string PersonName,
    Guid ProjectId,
    string ProjectName,
    Guid PocId,
    string PocName,
    FeedbackRequestStage Stage,
    PocResponseStatus Status);

// One Person currently Under Review with a Pending CatchUp (CBLT-244) —
// deliberately keyed off "has a Pending CatchUp", not "UnderReviewSince is
// set": after an EscalateFurther outcome, UnderReviewSince deliberately
// stays set (CBLT-241) but that CatchUp's own Status becomes Recorded, so
// the Person correctly stops appearing here until a fresh flag or ad-hoc
// trigger opens a new CatchUp for them.
public record FlaggedPersonEntry(
    Guid PersonId,
    string PersonName,
    Guid CatchUpId,
    CatchUpTriggerSource TriggerSource,
    DateTimeOffset PendingSince);
