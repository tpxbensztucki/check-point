namespace CheckPoint.Api.Domain;

// The top level of the org data model (spec Section 3). Inert grouping data with no
// owning role or behaviour beyond containing Practices.
public class Department
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    public ICollection<Practice> Practices { get; set; } = new List<Practice>();
}
