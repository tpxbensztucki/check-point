namespace CheckPoint.Api.Domain;

// The guest's actual feedback content (spec Section 6/8/10) — created exactly
// once per FeedbackRequest, at submission time, and never updated afterwards.
// There is deliberately no endpoint that edits an existing row: immutability is
// enforced by omission rather than by a guarded field.
public class FeedbackSubmission
{
    public Guid Id { get; set; }

    public Guid FeedbackRequestId { get; set; }
    public FeedbackRequest FeedbackRequest { get; set; } = null!;

    public required string DoingWell { get; set; }
    public required string NotDoingWell { get; set; }
    public required string NeedsToImprove { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
}
