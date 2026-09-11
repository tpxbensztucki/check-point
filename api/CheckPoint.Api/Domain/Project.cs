namespace CheckPoint.Api.Domain;

public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public ICollection<ProjectMembership> Memberships { get; set; } = new List<ProjectMembership>();
}
