using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Projects and Person<->Project membership (spec Section 3).
public class ProjectService(CheckPointDbContext db, TimeProvider timeProvider)
{
    public async Task<ProjectCreationResult> CreateProjectAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ProjectCreationResult.Invalid("Name is required.");
        }

        var project = new Project { Name = name };
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return ProjectCreationResult.Created(new ProjectResponse(project.Id, project.Name, project.Status));
    }

    public async Task<ProjectMembershipResult> AddPersonAsync(
        Guid projectId, Guid personId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectMembershipResult.ProjectNotFound($"No Project found with id {projectId}.");
        }

        var person = await db.People.SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return ProjectMembershipResult.Invalid($"No Person found with id {personId}.");
        }

        // Enrolling into the Project's New Starter cycle (spec Section 5) is
        // deferred until the cycle engine exists (Milestone 5) — this only
        // enforces the "not if they're a Leaver" precondition for now.
        if (person.Status == PersonStatus.Leaver)
        {
            return ProjectMembershipResult.Invalid("A Leaver cannot be added to a Project.");
        }

        var alreadyActiveMember = await db.ProjectMemberships.AnyAsync(
            m => m.ProjectId == projectId && m.PersonId == personId && m.RemovedAt == null, cancellationToken);
        if (alreadyActiveMember)
        {
            return ProjectMembershipResult.Invalid("Person is already an active member of this Project.");
        }

        var membership = new ProjectMembership
        {
            ProjectId = projectId,
            PersonId = personId,
            JoinedAt = timeProvider.GetUtcNow(),
        };
        db.ProjectMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);

        return ProjectMembershipResult.Added(
            new ProjectMembershipResponse(membership.Id, membership.ProjectId, membership.PersonId, membership.JoinedAt));
    }

    // Soft delete: RemovedAt is set rather than the row deleted, so the Person's
    // history on this Project (spec Section 3) is preserved.
    public async Task<ProjectMembershipRemovalResult> RemovePersonAsync(
        Guid projectId, Guid personId, CancellationToken cancellationToken = default)
    {
        var projectExists = await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken);
        if (!projectExists)
        {
            return ProjectMembershipRemovalResult.ProjectNotFound($"No Project found with id {projectId}.");
        }

        var membership = await db.ProjectMemberships.SingleOrDefaultAsync(
            m => m.ProjectId == projectId && m.PersonId == personId && m.RemovedAt == null, cancellationToken);
        if (membership is null)
        {
            return ProjectMembershipRemovalResult.MembershipNotFound(
                $"No active membership found for Person {personId} on Project {projectId}.");
        }

        membership.RemovedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return ProjectMembershipRemovalResult.Removed();
    }
}
