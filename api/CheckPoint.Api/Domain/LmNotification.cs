namespace CheckPoint.Api.Domain;

// A durable outbox row for the per-submission LM notification (spec Section 6/7).
// It is written in the very same SaveChangesAsync call as the FeedbackSubmission
// it belongs to, so the two either both persist or neither does — the
// notification can never be silently dropped even though the actual delivery
// mechanism (the Notifications epic's dispatch job) doesn't exist yet. SentAt
// stays null until that job picks the row up and delivers it.
public class LmNotification
{
    public Guid Id { get; set; }

    public Guid FeedbackSubmissionId { get; set; }
    public FeedbackSubmission FeedbackSubmission { get; set; } = null!;

    public Guid LineManagerId { get; set; }
    public Person LineManager { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
