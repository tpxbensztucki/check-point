using System.Security.Claims;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class OrgTreeEndpoints
{
    public static void MapOrgTreeEndpoints(this WebApplication app)
    {
        // Scope depends on which of the three roles the caller holds, combined
        // (a Person holding several sees the union of each) — not a plain role
        // check, so this only requires authentication; the scoping lives in
        // OrgTreeService.
        var group = app.MapGroup("/org-tree").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal caller, OrgTreeService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var tree = await service.GetOrgTreeForViewerAsync(
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return Results.Ok(tree);
        });
    }
}
