namespace CheckPoint.Api.Domain;

public enum FeedbackRequestStatus
{
    Scheduled,

    // Set by the dispatch job (spec Section 7, Milestone 7 — not implemented yet).
    Sent,

    // Set when the owning Project is completed before the request fires (spec
    // Section 5.1) — see ProjectService.CompleteProjectAsync.
    Cancelled,
}
