using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class AuditLogEndpoints
{
    public static void MapAuditLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/audit-log").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        group.MapGet("/", async (
            Guid? personId,
            Guid? viewerId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            AuditLogService service) =>
            Results.Ok(await service.GetLogAsync(personId, viewerId, from, to)));
    }
}
