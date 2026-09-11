using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Endpoints;

public record CreatePersonRequest(
    string FullName,
    Guid PracticeId,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId);

public record UpdatePersonRequest(
    string FullName,
    Guid PracticeId,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId);

public record PersonResponse(
    Guid Id,
    string FullName,
    PersonStatus Status,
    Guid PracticeId,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId);

// PracticeId is required when RoleName is RoleNames.PracticeLead (designates which
// Practice the Person owns as its lead) and ignored otherwise.
public record AssignRoleRequest(string RoleName, Guid? PracticeId);

public record PersonRolesResponse(Guid PersonId, IReadOnlyList<string> Roles);

public static class PersonEndpoints
{
    public static void MapPersonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/people").RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));

        group.MapPost("/", async (CreatePersonRequest request, CheckPointDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return Results.BadRequest("FullName is required.");
            }

            var practiceExists = await db.Practices.AnyAsync(p => p.Id == request.PracticeId);
            if (!practiceExists)
            {
                return Results.BadRequest($"No Practice found with id {request.PracticeId}.");
            }

            if (request.LineManagerId is { } lineManagerId &&
                !await db.People.AnyAsync(p => p.Id == lineManagerId))
            {
                return Results.BadRequest($"No Person found with id {lineManagerId} for LineManagerId.");
            }

            if (request.HeadOfPracticeId is { } headOfPracticeId &&
                !await db.People.AnyAsync(p => p.Id == headOfPracticeId))
            {
                return Results.BadRequest($"No Person found with id {headOfPracticeId} for HeadOfPracticeId.");
            }

            // Status always defaults to Employed and Roles are always empty at
            // creation — role assignment is a separate story (Assign/remove roles).
            var person = new Person
            {
                FullName = request.FullName,
                PracticeId = request.PracticeId,
                LineManagerId = request.LineManagerId,
                HeadOfPracticeId = request.HeadOfPracticeId,
            };
            db.People.Add(person);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/people/{person.Id}",
                new PersonResponse(
                    person.Id,
                    person.FullName,
                    person.Status,
                    person.PracticeId,
                    person.LineManagerId,
                    person.HeadOfPracticeId));
        });

        group.MapPut("/{personId:guid}", async (
            Guid personId, UpdatePersonRequest request, CheckPointDbContext db) =>
        {
            var person = await db.People.FindAsync(personId);
            if (person is null)
            {
                return Results.NotFound($"No Person found with id {personId}.");
            }

            if (string.IsNullOrWhiteSpace(request.FullName))
            {
                return Results.BadRequest("FullName is required.");
            }

            if (request.LineManagerId == personId)
            {
                return Results.BadRequest("A Person cannot be set as their own LineManager.");
            }

            var practiceExists = await db.Practices.AnyAsync(p => p.Id == request.PracticeId);
            if (!practiceExists)
            {
                return Results.BadRequest($"No Practice found with id {request.PracticeId}.");
            }

            if (request.LineManagerId is { } lineManagerId &&
                !await db.People.AnyAsync(p => p.Id == lineManagerId))
            {
                return Results.BadRequest($"No Person found with id {lineManagerId} for LineManagerId.");
            }

            if (request.HeadOfPracticeId is { } headOfPracticeId &&
                !await db.People.AnyAsync(p => p.Id == headOfPracticeId))
            {
                return Results.BadRequest($"No Person found with id {headOfPracticeId} for HeadOfPracticeId.");
            }

            // Recalculating the orphaned-person flag on Line Manager change (spec
            // Section 3) is deferred until that flag exists — see the Cross-practice
            // visibility and Orphaned Person detection story (CBLT-219).
            person.FullName = request.FullName;
            person.PracticeId = request.PracticeId;
            person.LineManagerId = request.LineManagerId;
            person.HeadOfPracticeId = request.HeadOfPracticeId;
            await db.SaveChangesAsync();

            return Results.Ok(new PersonResponse(
                person.Id,
                person.FullName,
                person.Status,
                person.PracticeId,
                person.LineManagerId,
                person.HeadOfPracticeId));
        });

        group.MapPost("/{personId:guid}/roles", async (
            Guid personId, AssignRoleRequest request, CheckPointDbContext db) =>
        {
            var person = await db.People.Include(p => p.Roles).SingleOrDefaultAsync(p => p.Id == personId);
            if (person is null)
            {
                return Results.NotFound($"No Person found with id {personId}.");
            }

            var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == request.RoleName);
            if (role is null)
            {
                return Results.BadRequest($"'{request.RoleName}' is not a valid role name.");
            }

            if (person.Roles.Any(r => r.Id == role.Id))
            {
                return Results.BadRequest($"Person already holds the '{request.RoleName}' role.");
            }

            Practice? practice = null;
            if (request.RoleName == RoleNames.PracticeLead)
            {
                if (request.PracticeId is not { } practiceId)
                {
                    return Results.BadRequest("PracticeId is required when assigning the Practice Lead role.");
                }

                practice = await db.Practices.SingleOrDefaultAsync(p => p.Id == practiceId);
                if (practice is null)
                {
                    return Results.BadRequest($"No Practice found with id {practiceId}.");
                }
            }

            person.Roles.Add(role);
            if (practice is not null)
            {
                practice.PracticeLeadId = person.Id;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new PersonRolesResponse(person.Id, person.Roles.Select(r => r.Name).ToList()));
        });

        group.MapDelete("/{personId:guid}/roles/{roleName}", async (
            Guid personId, string roleName, CheckPointDbContext db) =>
        {
            var person = await db.People.Include(p => p.Roles).SingleOrDefaultAsync(p => p.Id == personId);
            if (person is null)
            {
                return Results.NotFound($"No Person found with id {personId}.");
            }

            var role = person.Roles.SingleOrDefault(r => r.Name == roleName);
            if (role is null)
            {
                return Results.BadRequest($"Person does not hold the '{roleName}' role.");
            }

            person.Roles.Remove(role);

            // A Practice Lead who loses the role no longer owns any Practice as its
            // lead — clear every Practice pointing at them, not just one, since a
            // Person may lead more than one Practice.
            if (roleName == RoleNames.PracticeLead)
            {
                var ledPractices = await db.Practices.Where(p => p.PracticeLeadId == person.Id).ToListAsync();
                foreach (var practice in ledPractices)
                {
                    practice.PracticeLeadId = null;
                }
            }

            await db.SaveChangesAsync();

            return Results.Ok(new PersonRolesResponse(person.Id, person.Roles.Select(r => r.Name).ToList()));
        });
    }
}
