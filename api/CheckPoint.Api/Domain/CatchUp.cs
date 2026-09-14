namespace CheckPoint.Api.Domain;

// A pending Line Manager <-> Practice Lead conversation triggered by either a
// flagged check-in or a directly-triggered ad-hoc review (spec Section 5.3).
// Who the LM and Practice Lead actually are isn't stored here — like
// Person.IsOrphaned elsewhere in this codebase, that's resolved live via
// Person.LineManagerId / Practice.PracticeLeadId at read time, so it can never
// go stale if either changes after the flag. Internal-only (never crosses into
// the guest-facing world), so unlike MagicLink.FeedbackRequestId this carries
// a real FK to FeedbackRequest — but a nullable one (CBLT-240): an ad-hoc
// review has no check-in reference at all, by design.
public class CatchUp
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public Guid? FeedbackRequestId { get; set; }
    public FeedbackRequest? FeedbackRequest { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public CatchUpStatus Status { get; set; } = CatchUpStatus.Pending;

    // Set together when the outcome is recorded (CBLT-241) — all three stay
    // null while Status is Pending.
    public CatchUpOutcomeType? OutcomeType { get; set; }
    public string? OutcomeNotes { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
}
