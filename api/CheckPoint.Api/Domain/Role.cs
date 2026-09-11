namespace CheckPoint.Api.Domain;

// One of the fixed set of roles a Person can hold (see RoleNames). Rows are seeded
// by migration, not created at runtime — the set of valid roles is closed.
public class Role
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public ICollection<Person> People { get; set; } = new List<Person>();
}
