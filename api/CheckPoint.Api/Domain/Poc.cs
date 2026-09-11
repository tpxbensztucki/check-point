namespace CheckPoint.Api.Domain;

// A Point of Contact captured against one Person's membership on one Project
// (spec Section 3) — never an existing system user, just a name/email/relationship
// snapshot, since guests have no standing account.
public class Poc
{
    public Guid Id { get; set; }

    public Guid ProjectMembershipId { get; set; }
    public ProjectMembership ProjectMembership { get; set; } = null!;

    public required string Name { get; set; }
    public required string Email { get; set; }
    public PocRelationship Relationship { get; set; }
    public PocRole Role { get; set; }
}
