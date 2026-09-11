namespace CheckPoint.Api.Domain;

// Grants a guest POC one-time, time-limited access to a single feedback request
// (spec Section 9), with no standing account and no access to anything else.
// FeedbackRequestId is an opaque reference — the FeedbackRequest entity itself
// belongs to a later epic (Milestone 5: Feedback Cycle Engine); this mechanism is
// deliberately independent of it.
public class MagicLink
{
    public Guid Id { get; set; }
    public required string Token { get; set; }
    public Guid FeedbackRequestId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
