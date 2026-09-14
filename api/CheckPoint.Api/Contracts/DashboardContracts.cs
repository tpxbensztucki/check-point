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
