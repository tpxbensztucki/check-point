using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Endpoints;

public record CreateDepartmentRequest(string Name);
public record CreatePracticeRequest(string Name);
public record DepartmentResponse(Guid Id, string Name);
public record PracticeResponse(Guid Id, string Name, Guid DepartmentId);

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
    }
}
