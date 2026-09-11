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
}
