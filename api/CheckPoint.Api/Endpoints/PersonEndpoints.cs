using System.Security.Claims;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class PersonEndpoints
{
    public static void MapPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/people").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        group.MapPost("/", async (CreatePersonRequest request, PersonService service) =>
        {
            var result = await service.CreateAsync(request);
            return result.Status switch
            {
                PersonCreationStatus.Created => Results.Created($"/people/{result.Person!.Id}", result.Person),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapPut("/{personId:guid}", async (Guid personId, UpdatePersonRequest request, PersonService service) =>
        {
            var result = await service.UpdateAsync(personId, request);
            return result.Status switch
            {
                PersonUpdateStatus.Updated => Results.Ok(result.Person),
                PersonUpdateStatus.NotFound => Results.NotFound(result.Error),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapPost("/{personId:guid}/roles", async (
            Guid personId, AssignRoleRequest request, PersonService service) =>
        {
            var result = await service.AssignRoleAsync(personId, request);
            return result.Status switch
            {
                RoleAssignmentStatus.Assigned => Results.Ok(result.Roles),
                RoleAssignmentStatus.PersonNotFound => Results.NotFound(result.Error),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapDelete("/{personId:guid}/roles/{roleName}", async (
            Guid personId, string roleName, PersonService service) =>
        {
            var result = await service.RemoveRoleAsync(personId, roleName);
            return result.Status switch
            {
                RoleRemovalStatus.Removed => Results.Ok(result.Roles),
                RoleRemovalStatus.PersonNotFound => Results.NotFound(result.Error),
                _ => Results.BadRequest(result.Error),
            };
        });

        // Setting Leaver is available to Admin (any Person) and to a Line Manager
        // for their own reports, unlike the rest of /people which is Admin-only, so
        // it needs its own group with a looser authorization requirement; the
        // ownership check lives in the service.
        var leaverGroup = app.MapGroup("/people").RequireAuthorization();

        leaverGroup.MapPost("/{personId:guid}/leaver", async (
            Guid personId, ClaimsPrincipal caller, PersonService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.MarkAsLeaverForViewerAsync(
                personId, callerId, caller.IsInRole(RoleNames.Admin), caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                LeaverTransitionStatus.MarkedAsLeaver => Results.Ok(result.Person),
                LeaverTransitionStatus.PersonNotFound => Results.NotFound(result.Error),
                LeaverTransitionStatus.Forbidden => Results.Forbid(),
                _ => Results.BadRequest(result.Error),
            };
        });
    }
}
