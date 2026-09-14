using System.Security.Claims;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/projects").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        // Visibility follows the same role scoping as the org tree, not a plain
        // role check, so this needs its own group requiring only authentication;
        // the ownership check lives in ProjectService.
        var personProjectsGroup = app.MapGroup("/people/{personId:guid}/projects").RequireAuthorization();

        personProjectsGroup.MapGet("/", async (Guid personId, ClaimsPrincipal caller, ProjectService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetProjectsForPersonAsync(
                personId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PersonProjectsStatus.Success => Results.Ok(result.Projects),
                PersonProjectsStatus.PersonNotFound => Results.NotFound(result.Error),
                _ => Results.Forbid(),
            };
        });

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

        // Filtered-list visibility (spec Section 8, CBLT-238), not a plain role
        // check, so this needs its own group requiring only authentication —
        // same reasoning as personProjectsGroup above.
        var pocPatternsGroup = app.MapGroup("/projects/{projectId:guid}").RequireAuthorization();

        pocPatternsGroup.MapGet("/poc-response-patterns", async (
            Guid projectId, ClaimsPrincipal caller, PocResponseHistoryService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetProjectPocPatternsAsync(
                projectId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                ProjectPocPatternsStatus.Success => Results.Ok(result.Entries),
                ProjectPocPatternsStatus.ProjectNotFound => Results.NotFound(result.Error),
                _ => Results.Problem(),
            };
        });
    }
}
