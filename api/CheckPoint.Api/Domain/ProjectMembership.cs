namespace CheckPoint.Api.Domain;

// Explicit join entity (rather than an implicit many-to-many like Person<->Role)
// because a Person's per-Project feedback cycle (Feedback Cycle Engine epic) needs
// somewhere to attach state to a specific Person-Project pairing, and "removed
// without deleting history" (spec Section 3) needs a row that survives removal —
// RemovedAt is set rather than the row being deleted.
public class ProjectMembership
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }

    // Null while still in the New Starter cycle; set once by
    // FeedbackCycleService.HandleFeedbackRequestCompletedAsync (CBLT-228) when the
    // final New Starter request (NewStarterWeek8) concludes, and never cleared
    // again — this is both the transition guard (idempotency) and, for CBLT-229,
    // the anchor date FY-quarter General-cycle scheduling will count from.
    public DateTimeOffset? GeneralCycleEnrolledAt { get; set; }

    public ICollection<Poc> Pocs { get; set; } = new List<Poc>();
}
