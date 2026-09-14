namespace CheckPoint.Api.Domain;

// An immutable record of someone viewing or exporting a Person's feedback
// content (spec Section 11, CBLT-249) — for GDPR accountability and to trace
// "who saw what" if a dispute arises. No update/delete path exists through
// normal application access; AuditLogService only ever inserts. Deliberately
// carries no reference to any specific FeedbackSubmission — only who was
// viewed, not which submissions — so an entry survives intact even after the
// 6-month post-leaver retention job (CBLT-248) removes the underlying
// feedback content it once referred to.
public class AuditLogEntry
{
    public Guid Id { get; set; }

    public Guid ViewerId { get; set; }
    public Person Viewer { get; set; } = null!;

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public AuditAction Action { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
