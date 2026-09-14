using System.Security.Claims;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        // Scoping (Admin/Practice Lead/Line Manager union) lives in
        // DashboardService, same pattern as /org-tree — not a plain role
        // check.
        var group = app.MapGroup("/dashboard").RequireAuthorization();

        group.MapGet("/outstanding-requests", async (ClaimsPrincipal caller, DashboardService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var entries = await service.GetOutstandingRequestsAsync(
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return Results.Ok(entries);
        });
    }
}
