using System.Security.Claims;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

public static class DepartmentEndpoints
{
    public static void MapDepartmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/departments").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        group.MapPost("/", async (CreateDepartmentRequest request, DepartmentService service) =>
        {
            var result = await service.CreateDepartmentAsync(request.Name);
            return result.Status switch
            {
                DepartmentCreationStatus.Created =>
                    Results.Created($"/departments/{result.Department!.Id}", result.Department),
                _ => Results.BadRequest(result.Error),
            };
        });

        group.MapPost("/{departmentId:guid}/practices", async (
            Guid departmentId, CreatePracticeRequest request, DepartmentService service) =>
        {
            var result = await service.CreatePracticeAsync(departmentId, request.Name);
            return result.Status switch
            {
                PracticeCreationStatus.Created => Results.Created(
                    $"/departments/{departmentId}/practices/{result.Practice!.Id}", result.Practice),
                PracticeCreationStatus.DepartmentNotFound => Results.NotFound(result.Error),
                _ => Results.BadRequest(result.Error),
            };
        });

        // Visibility follows Practice tags, not reporting lines (spec Section 2):
        // Admin sees any Practice's people, a Practice Lead only their own
        // Practice's — not a plain role check, so this route needs its own group
        // requiring only authentication; the ownership check lives in the service.
        var practiceViewGroup = app.MapGroup("/practices").RequireAuthorization();

        practiceViewGroup.MapGet("/{practiceId:guid}/people", async (
            Guid practiceId, ClaimsPrincipal caller, DepartmentService service) =>
        {
            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = await service.GetPracticePeopleForViewerAsync(
                practiceId, callerId, caller.IsInRole(RoleNames.Admin), caller.IsInRole(RoleNames.PracticeLead));

            return result.Status switch
            {
                PracticePeopleViewStatus.Success => Results.Ok(result.People),
                PracticePeopleViewStatus.PracticeNotFound => Results.NotFound(result.Error),
                _ => Results.Forbid(),
            };
        });
    }
}
