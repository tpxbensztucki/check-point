using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Endpoints;

public record CreatePersonRequest(
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
    }
}
