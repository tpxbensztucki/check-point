using System.Security.Claims;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class FeedbackRequestEndpoints
{
    public static void MapFeedbackRequestEndpoints(this WebApplication app)
    {
        // Admin, the Practice Lead of the target Person's Practice, or the Line
        // Manager of the target Person — not a plain role check, so this only
        // requires authentication; the ownership check lives in RequestDispatchService.
        var group = app.MapGroup("/feedback-requests").RequireAuthorization();

        group.MapPost("/{id:guid}/dispatch", async (Guid id, ClaimsPrincipal caller, RequestDispatchService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.DispatchManuallyAsync(
                id,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                RequestDispatchStatus.Dispatched => Results.Ok(),
                RequestDispatchStatus.RequestNotFound => Results.NotFound(result.Error),
                RequestDispatchStatus.Forbidden => Results.Forbid(),
                RequestDispatchStatus.NotCurrentlyScheduled => Results.Conflict(result.Error),
                RequestDispatchStatus.NoPocsAssigned => Results.BadRequest(result.Error),
                _ => Results.Problem(),
            };
        });

        group.MapPost("/{id:guid}/pocs/{pocId:guid}/remind", async (
            Guid id, Guid pocId, ClaimsPrincipal caller, RequestDispatchService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.SendReminderAsync(
                id,
                pocId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                ReminderStatus.Sent => Results.Ok(),
                ReminderStatus.RequestNotFound => Results.NotFound(result.Error),
                ReminderStatus.PocNotFound => Results.NotFound(result.Error),
                ReminderStatus.Forbidden => Results.Forbid(),
                ReminderStatus.NotYetDispatched => Results.Conflict(result.Error),
                ReminderStatus.AlreadySubmitted => Results.Conflict(result.Error),
                _ => Results.Problem(),
            };
        });

        group.MapGet("/{id:guid}/pocs", async (Guid id, ClaimsPrincipal caller, RequestDispatchService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetPocStatusesAsync(
                id,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PocStatusViewStatus.Success => Results.Ok(result.Entries),
                PocStatusViewStatus.RequestNotFound => Results.NotFound(result.Error),
                PocStatusViewStatus.Forbidden => Results.Forbid(),
                _ => Results.Problem(),
            };
        });
    }
}
