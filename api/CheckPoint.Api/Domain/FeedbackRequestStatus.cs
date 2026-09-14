namespace CheckPoint.Api.Domain;

public enum FeedbackRequestStatus
{
    Scheduled,

    // Set by the dispatch job (spec Section 7, Milestone 7 — not implemented yet).
    Sent,

    // Set when the owning Project is completed before the request fires (spec
    // Section 5.1) — see ProjectService.CompleteProjectAsync.
    Cancelled,

    // Deliberately no "Responded"/"Completed" value here: a request can have
    // several currently-assigned POCs, each submitting independently (CBLT-302),
    // and CBLT-237 confirms per-POC outcomes (Submitted/No Response) are tracked
    // per POC, never aggregated into a single status on the request as a whole.
    // Whether a given POC has responded is answered by whether a
    // FeedbackSubmission row exists for that (FeedbackRequestId, PocId) pair —
    // not stored here.
}
