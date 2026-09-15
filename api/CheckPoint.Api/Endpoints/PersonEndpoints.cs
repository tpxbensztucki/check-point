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

        group.MapGet("/", async (PersonService service) => Results.Ok(await service.GetAllAsync()));

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

        // Three-way (Admin, the Person's own Line Manager, or their Practice
        // Lead, per CBLT-240's own AC) — same looser group as leaverGroup above.
        leaverGroup.MapPost("/{personId:guid}/ad-hoc-review", async (
            Guid personId, ClaimsPrincipal caller, FeedbackCycleService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.TriggerAdHocReviewAsync(
                personId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                AdHocReviewStatus.Triggered => Results.Ok(result.Review),
                AdHocReviewStatus.PersonNotFound => Results.NotFound(result.Error),
                AdHocReviewStatus.Forbidden => Results.Forbid(),
                _ => Results.Problem(),
            };
        });

        // Scoped single-Person profile read backing the new Person-profile
        // frontend page — Practice Lead and Line Manager have no way to view
        // a Person at all otherwise, since GET /people above is Admin-only.
        // Same three-way scoping as the ad-hoc-review route.
        leaverGroup.MapGet("/{personId:guid}", async (
            Guid personId, ClaimsPrincipal caller, PersonService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetForViewerAsync(
                personId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PersonProfileStatus.Success => Results.Ok(result.Person),
                PersonProfileStatus.PersonNotFound => Results.NotFound(result.Error),
                PersonProfileStatus.Forbidden => Results.Forbid(),
                _ => Results.Problem(),
            };
        });

        // Three-way, same scoping as the ad-hoc-review route above (spec
        // Section 5.3, CBLT-242: "Admin: all; Practice Lead: own practice; LM:
        // own reports").
        leaverGroup.MapGet("/{personId:guid}/catch-ups", async (
            Guid personId, ClaimsPrincipal caller, CatchUpService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetHistoryAsync(
                personId,
                callerId,
                caller.IsInRole(RoleNames.Admin),
                caller.IsInRole(RoleNames.PracticeLead),
                caller.IsInRole(RoleNames.LineManager));

            return result.Status switch
            {
                PersonCatchUpHistoryStatus.Success => Results.Ok(result.History),
                PersonCatchUpHistoryStatus.PersonNotFound => Results.NotFound(result.Error),
                PersonCatchUpHistoryStatus.Forbidden => Results.Forbid(),
                _ => Results.Problem(),
            };
        });
    }
}
