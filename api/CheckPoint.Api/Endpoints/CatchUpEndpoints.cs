using System.Security.Claims;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class CatchUpEndpoints
{
    public static void MapCatchUpEndpoints(this WebApplication app)
    {
        // Three-way (Admin, the Person's own Line Manager, or their Practice
        // Lead) — not a plain role check, so this only requires authentication;
        // the ownership check lives in CatchUpService.
        var group = app.MapGroup("/catch-ups").RequireAuthorization();

        group.MapPost("/{catchUpId:guid}/outcome", async (
            Guid catchUpId, RecordCatchUpOutcomeRequest request, ClaimsPrincipal caller, CatchUpService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.RecordOutcomeAsync(
                catchUpId,
                request.OutcomeType,
                request.Notes,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                RecordOutcomeStatus.Recorded => Results.Ok(result.CatchUp),
                RecordOutcomeStatus.CatchUpNotFound => Results.NotFound(result.Error),
                RecordOutcomeStatus.Forbidden => Results.Forbid(),
                RecordOutcomeStatus.AlreadyRecorded => Results.Conflict(result.Error),
                RecordOutcomeStatus.Invalid => Results.BadRequest(result.Error),
                _ => Results.Problem(),
            };
        });
    }
}
