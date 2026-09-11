namespace CheckPoint.Api.Domain;

// The second level of the org data model (spec Section 3) — always nested under a
// Department; cannot exist without one.
public class Practice
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public ICollection<Person> People { get; set; } = new List<Person>();
}
