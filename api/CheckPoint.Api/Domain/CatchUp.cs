namespace CheckPoint.Api.Domain;

// A pending Line Manager <-> Practice Lead conversation triggered by a flagged
// check-in (spec Section 5.3). Who the LM and Practice Lead actually are isn't
// stored here — like Person.IsOrphaned elsewhere in this codebase, that's
// resolved live via Person.LineManagerId / Practice.PracticeLeadId at read time,
// so it can never go stale if either changes after the flag. Internal-only (never
// crosses into the guest-facing world), so unlike MagicLink.FeedbackRequestId this
// carries a real FK to FeedbackRequest.
public class CatchUp
{
    public Guid Id { get; set; }

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public Guid FeedbackRequestId { get; set; }
    public FeedbackRequest FeedbackRequest { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public CatchUpStatus Status { get; set; } = CatchUpStatus.Pending;
}
