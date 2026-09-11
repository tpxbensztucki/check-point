namespace CheckPoint.Api.Domain;

// The single account type for every logged-in user (spec Section 2) — Admin,
// Practice Lead, and Line Manager are Roles held by a Person, not separate account
// types. A Person with no Roles has no login capability. Fields beyond identity and
// roles (department, line manager, leaver status, etc.) belong to later stories
// (Milestone 3: Org & People Management).
public class Person
{
    public Guid Id { get; set; }
    public required string FullName { get; set; }

    public ICollection<Role> Roles { get; set; } = new List<Role>();
}
