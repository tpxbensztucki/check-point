namespace CheckPoint.Api.Domain;

// Grants one specific guest POC one-time, time-limited access to a single
// feedback request (spec Section 9), with no standing account and no access to
// anything else. FeedbackRequestId is an opaque reference — the FeedbackRequest
// entity itself belongs to a later epic (Milestone 5: Feedback Cycle Engine);
// this mechanism is deliberately independent of it.
//
// PocId (added CBLT-302) is a real FK: a request can have several currently
// assigned POCs, each of whom gets their own link and gives independent
// feedback, so the link must identify which POC it was issued to.
public class MagicLink
{
    public Guid Id { get; set; }
    public required string Token { get; set; }
    public Guid FeedbackRequestId { get; set; }
    public Guid PocId { get; set; }
    public Poc Poc { get; set; } = null!;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }

    // Set when a manual reminder (CBLT-236) issues a fresh link for the same
    // (FeedbackRequest, Poc) pair — distinct from UsedAt, which specifically means
    // "consumed by a submission". A superseded link was never used; it was just
    // replaced by a newer one.
    public DateTimeOffset? InvalidatedAt { get; set; }
}
