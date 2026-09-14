namespace CheckPoint.Api.Contracts;

// Per-POC outcome for one FeedbackRequest (spec Section 5.4, 9) — deliberately
// never a single status on the request as a whole, since different POCs on the
// same request can be in different states (CBLT-237).
public enum PocResponseStatus
{
    NotYetSent,
    Sent,
    Submitted,
    NoResponse,
    Cancelled,
}

public record PocResponseStatusEntry(Guid PocId, string PocName, string PocEmail, PocResponseStatus Status);

// A single POC's outcome history across every request they were ever
// dispatched to on their ProjectMembership (spec Section 5.4, CBLT-238) — not
// just the single most recent one.
public record PocResponseHistoryEntry(Guid FeedbackRequestId, DateTimeOffset ScheduledFor, PocResponseStatus Status);

public record PocResponseHistoryResponse(
    Guid PocId,
    string PocName,
    int ConsecutiveNoResponseCount,
    int TotalNoResponseCount,
    IReadOnlyList<PocResponseHistoryEntry> Entries);

// One row in a Project's "which POCs have a pattern of non-response" view
// (CBLT-238) — deliberately no fixed "pattern" threshold: the raw counts are
// returned, same live-computed-not-stored philosophy as CBLT-237.
public record ProjectPocPatternEntry(
    Guid PocId,
    string PocName,
    string PersonName,
    int ConsecutiveNoResponseCount,
    int TotalNoResponseCount);
