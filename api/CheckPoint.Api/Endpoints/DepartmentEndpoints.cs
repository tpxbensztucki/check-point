using System.Security.Claims;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Endpoints;

public record CreateDepartmentRequest(string Name);
public record CreatePracticeRequest(string Name);
public record DepartmentResponse(Guid Id, string Name);
public record PracticeResponse(Guid Id, string Name, Guid DepartmentId);

// IsOrphaned is true when LineManagerId is unset, or the Line Manager's own
// PracticeId differs from this Person's (spec Section 2) — computed on every read,
// not stored, so it can never go stale when either Person's Practice or Line
// Manager changes.
public record PracticePersonResponse(
    Guid Id,
    string FullName,
    PersonStatus Status,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId,
    bool IsOrphaned);

public static class DepartmentEndpoints
{
    public static void MapDepartmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/departments").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        group.MapPost("/", async (CreateDepartmentRequest request, CheckPointDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            var department = new Department { Name = request.Name };
            db.Departments.Add(department);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/departments/{department.Id}",
                new DepartmentResponse(department.Id, department.Name));
        });

        group.MapPost("/{departmentId:guid}/practices", async (
            Guid departmentId, CreatePracticeRequest request, CheckPointDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            var departmentExists = await db.Departments.AnyAsync(d => d.Id == departmentId);
            if (!departmentExists)
            {
                return Results.NotFound($"No Department found with id {departmentId}.");
            }

            var practice = new Practice { Name = request.Name, DepartmentId = departmentId };
            db.Practices.Add(practice);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/departments/{departmentId}/practices/{practice.Id}",
                new PracticeResponse(practice.Id, practice.Name, practice.DepartmentId));
        });

        // Visibility follows Practice tags, not reporting lines (spec Section 2):
        // Admin sees any Practice's people, a Practice Lead only their own
        // Practice's — not a plain role check, so this route needs its own group
        // requiring only authentication plus a manual ownership check below.
        var practiceViewGroup = app.MapGroup("/practices").RequireAuthorization();

        practiceViewGroup.MapGet("/{practiceId:guid}/people", async (
            Guid practiceId, ClaimsPrincipal caller, CheckPointDbContext db) =>
        {
            var practice = await db.Practices.SingleOrDefaultAsync(p => p.Id == practiceId);
            if (practice is null)
            {
                return Results.NotFound($"No Practice found with id {practiceId}.");
            }

            var callerId = Guid.Parse(caller.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isLeadOfThisPractice = caller.IsInRole(RoleNames.PracticeLead) && practice.PracticeLeadId == callerId;
            if (!caller.IsInRole(RoleNames.Admin) && !isLeadOfThisPractice)
            {
                return Results.Forbid();
            }

            // Filtering to this PracticeId before projecting is what keeps a Line
            // Manager tagged to a different Practice out of the results, even
            // though one of their reports (tagged here) is included and flagged
            // Orphaned.
            var people = await db.People
                .Where(p => p.PracticeId == practiceId)
                .Select(p => new PracticePersonResponse(
                    p.Id,
                    p.FullName,
                    p.Status,
                    p.LineManagerId,
                    p.HeadOfPracticeId,
                    p.LineManagerId == null || p.LineManager!.PracticeId != p.PracticeId))
                .ToListAsync();

            return Results.Ok(people);
        });
    }
}
