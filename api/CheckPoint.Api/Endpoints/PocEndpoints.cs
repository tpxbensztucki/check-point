using System.Security.Claims;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class PocEndpoints
{
    public static void MapPocEndpoints(this WebApplication app)
    {
        // Admin, the Practice Lead of the target Person's Practice, or the Line
        // Manager of the target Person — not a plain role check, so this only
        // requires authentication; the ownership check lives in PocService.
        var group = app.MapGroup("/projects/{projectId:guid}/people/{personId:guid}/pocs")
            .RequireAuthorization();

        group.MapPost("/", async (
            Guid projectId, Guid personId, CreatePocRequest request, ClaimsPrincipal caller, PocService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.AssignPocAsync(
                projectId,
                personId,
                request,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PocAssignmentStatus.Assigned => Results.Created(
                    $"/projects/{projectId}/people/{personId}/pocs", result.Pocs),
                PocAssignmentStatus.MembershipNotFound => Results.NotFound(result.Error),
                PocAssignmentStatus.Forbidden => Results.Forbid(),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapPut("/{pocId:guid}", async (
            Guid projectId, Guid personId, Guid pocId, CreatePocRequest request, ClaimsPrincipal caller, PocService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.UpdatePocAsync(
                projectId,
                personId,
                pocId,
                request,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PocMutationStatus.Success => Results.Ok(result.Pocs),
                PocMutationStatus.MembershipNotFound => Results.NotFound(result.Error),
                PocMutationStatus.PocNotFound => Results.NotFound(result.Error),
                PocMutationStatus.Forbidden => Results.Forbid(),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapDelete("/{pocId:guid}", async (
            Guid projectId, Guid personId, Guid pocId, ClaimsPrincipal caller, PocService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.RemovePocAsync(
                projectId,
                personId,
                pocId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PocMutationStatus.Success => Results.Ok(result.Pocs),
                PocMutationStatus.MembershipNotFound => Results.NotFound(result.Error),
                PocMutationStatus.PocNotFound => Results.NotFound(result.Error),
                PocMutationStatus.Forbidden => Results.Forbid(),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapGet("/", async (Guid projectId, Guid personId, ClaimsPrincipal caller, PocService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetPocsForViewerAsync(
                projectId,
                personId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PocViewStatus.Success => Results.Ok(result.Pocs),
                PocViewStatus.MembershipNotFound => Results.NotFound(result.Error),
                _ => Results.Forbid(),
            };
        });

        // Not nested under the group above — a Poc's response history isn't
        // scoped to a specific /projects/{projectId}/people/{personId} route,
        // only to the Poc itself (CBLT-238).
        app.MapGet("/pocs/{pocId:guid}/response-history", async (
            Guid pocId, ClaimsPrincipal caller, PocResponseHistoryService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetPocHistoryAsync(
                pocId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PocHistoryStatus.Success => Results.Ok(result.History),
                PocHistoryStatus.PocNotFound => Results.NotFound(result.Error),
                _ => Results.Forbid(),
            };
        }).RequireAuthorization();
    }
}
