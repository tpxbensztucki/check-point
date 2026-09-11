namespace CheckPoint.Api.Domain;

// A scheduled feedback request for one Person's membership on one Project (spec
// Section 5). Deliberately carries no snapshot of which POCs to send to — the
// dispatch job (Milestone 7) resolves the Project's currently assigned POCs at
// send time, not whichever were assigned when this row was scheduled.
//
// MagicLink.FeedbackRequestId remains a bare, unconstrained Guid rather than a
// real FK to this entity — that decoupling was a deliberate design choice from
// CBLT-213 (see MagicLinkService's own doc comment) and isn't revisited here.
public class FeedbackRequest
{
    public Guid Id { get; set; }

    public Guid ProjectMembershipId { get; set; }
    public ProjectMembership ProjectMembership { get; set; } = null!;

    public DateTimeOffset ScheduledFor { get; set; }
    public FeedbackRequestStatus Status { get; set; } = FeedbackRequestStatus.Scheduled;
}
