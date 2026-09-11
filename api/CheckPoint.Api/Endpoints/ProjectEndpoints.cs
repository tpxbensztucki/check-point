using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/projects").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        group.MapPost("/", async (CreateProjectRequest request, ProjectService service) =>
        {
            var result = await service.CreateProjectAsync(request.Name);
            return result.Status switch
            {
                ProjectCreationStatus.Created => Results.Created($"/projects/{result.Project!.Id}", result.Project),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapPost("/{projectId:guid}/complete", async (Guid projectId, ProjectService service) =>
        {
            var result = await service.CompleteProjectAsync(projectId);
            return result.Status switch
            {
                ProjectCompletionStatus.Completed => Results.Ok(result.Project),
                ProjectCompletionStatus.ProjectNotFound => Results.NotFound(result.Error),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapPost("/{projectId:guid}/people", async (
            Guid projectId, AddPersonToProjectRequest request, ProjectService service) =>
        {
            var result = await service.AddPersonAsync(projectId, request.PersonId);
            return result.Status switch
            {
                ProjectMembershipStatus.Added => Results.Created(
                    $"/projects/{projectId}/people/{request.PersonId}", result.Membership),
                ProjectMembershipStatus.ProjectNotFound => Results.NotFound(result.Error),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapDelete("/{projectId:guid}/people/{personId:guid}", async (
            Guid projectId, Guid personId, ProjectService service) =>
        {
            var result = await service.RemovePersonAsync(projectId, personId);
            return result.Status switch
            {
                ProjectMembershipRemovalStatus.Removed => Results.NoContent(),
                _ => Results.NotFound(result.Error),
            };
        });
    }
}
