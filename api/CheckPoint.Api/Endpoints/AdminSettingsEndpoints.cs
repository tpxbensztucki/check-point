using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class AdminSettingsEndpoints
{
    public static void MapAdminSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/settings").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        group.MapGet("/", async (AdminSettingsService service) => Results.Ok(await service.GetAsync()));

        group.MapPut("/", async (UpdateAdminSettingsRequest request, AdminSettingsService service) =>
        {
            var result = await service.UpdateAsync(request);
            return result.Status switch
            {
                AdminSettingsUpdateStatus.Updated => Results.Ok(result.Settings),
                _ => Results.BadRequest(result.Error),
            };
        });
    }
}
