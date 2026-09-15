using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// People, their Roles, and the Leaver transition (spec Sections 2-4).
public class PersonService(CheckPointDbContext db, TimeProvider timeProvider)
{
    public async Task<PersonCreationResult> CreateAsync(
        CreatePersonRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return PersonCreationResult.Invalid("FullName is required.");
        }

        // Email became required by CBLT-327 — it's load-bearing for CBLT-235's
        // per-submission LM notification, and something an Admin should always
        // be capturing up front rather than as an afterthought. The database
        // column stays nullable (Person.cs) for back-compat with any
        // pre-existing rows created before this validation existed.
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            return PersonCreationResult.Invalid("A valid Email is required.");
        }

        if (!await db.Practices.AnyAsync(p => p.Id == request.PracticeId, cancellationToken))
        {
            return PersonCreationResult.Invalid($"No Practice found with id {request.PracticeId}.");
        }

        if (request.LineManagerId is { } lineManagerId &&
            !await db.People.AnyAsync(p => p.Id == lineManagerId, cancellationToken))
        {
            return PersonCreationResult.Invalid($"No Person found with id {lineManagerId} for LineManagerId.");
        }

        if (request.HeadOfPracticeId is { } headOfPracticeId &&
            !await db.People.AnyAsync(p => p.Id == headOfPracticeId, cancellationToken))
        {
            return PersonCreationResult.Invalid($"No Person found with id {headOfPracticeId} for HeadOfPracticeId.");
        }

        // Status always defaults to Employed and Roles are always empty at
        // creation — role assignment is a separate operation (AssignRoleAsync).
        var person = new Person
        {
            FullName = request.FullName,
            PracticeId = request.PracticeId,
            LineManagerId = request.LineManagerId,
            HeadOfPracticeId = request.HeadOfPracticeId,
            Email = request.Email,
        };
        db.People.Add(person);
        await db.SaveChangesAsync(cancellationToken);

        return PersonCreationResult.Created(ToResponse(person));
    }

    public async Task<PersonUpdateResult> UpdateAsync(
        Guid personId, UpdatePersonRequest request, CancellationToken cancellationToken = default)
    {
        var person = await db.People.FindAsync([personId], cancellationToken);
        if (person is null)
        {
            return PersonUpdateResult.NotFound($"No Person found with id {personId}.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return PersonUpdateResult.Invalid("FullName is required.");
        }

        // See CreateAsync's equivalent check for why this became required (CBLT-327).
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            return PersonUpdateResult.Invalid("A valid Email is required.");
        }

        if (request.LineManagerId == personId)
        {
            return PersonUpdateResult.Invalid("A Person cannot be set as their own LineManager.");
        }

        if (!await db.Practices.AnyAsync(p => p.Id == request.PracticeId, cancellationToken))
        {
            return PersonUpdateResult.Invalid($"No Practice found with id {request.PracticeId}.");
        }

        if (request.LineManagerId is { } lineManagerId &&
            !await db.People.AnyAsync(p => p.Id == lineManagerId, cancellationToken))
        {
            return PersonUpdateResult.Invalid($"No Person found with id {lineManagerId} for LineManagerId.");
        }

        if (request.HeadOfPracticeId is { } headOfPracticeId &&
            !await db.People.AnyAsync(p => p.Id == headOfPracticeId, cancellationToken))
        {
            return PersonUpdateResult.Invalid($"No Person found with id {headOfPracticeId} for HeadOfPracticeId.");
        }

        // Recalculating the orphaned-person flag on Line Manager change (spec
        // Section 3) needs no action here — it's computed live wherever a Person
        // is read for viewing, see DepartmentService.GetPracticePeopleForViewerAsync.
        person.FullName = request.FullName;
        person.PracticeId = request.PracticeId;
        person.LineManagerId = request.LineManagerId;
        person.HeadOfPracticeId = request.HeadOfPracticeId;
        person.Email = request.Email;
        await db.SaveChangesAsync(cancellationToken);

        return PersonUpdateResult.Updated(ToResponse(person));
    }

    public async Task<RoleAssignmentResult> AssignRoleAsync(
        Guid personId, AssignRoleRequest request, CancellationToken cancellationToken = default)
    {
        var person = await db.People.Include(p => p.Roles).SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return RoleAssignmentResult.PersonNotFound($"No Person found with id {personId}.");
        }

        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == request.RoleName, cancellationToken);
        if (role is null)
        {
            return RoleAssignmentResult.Invalid($"'{request.RoleName}' is not a valid role name.");
        }

        if (person.Roles.Any(r => r.Id == role.Id))
        {
            return RoleAssignmentResult.Invalid($"Person already holds the '{request.RoleName}' role.");
        }

        Practice? practice = null;
        if (request.RoleName == RoleNames.PracticeLead)
        {
            if (request.PracticeId is not { } practiceId)
            {
                return RoleAssignmentResult.Invalid("PracticeId is required when assigning the Practice Lead role.");
            }

            practice = await db.Practices.SingleOrDefaultAsync(p => p.Id == practiceId, cancellationToken);
            if (practice is null)
            {
                return RoleAssignmentResult.Invalid($"No Practice found with id {practiceId}.");
            }
        }

        person.Roles.Add(role);
        if (practice is not null)
        {
            practice.PracticeLeadId = person.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        return RoleAssignmentResult.Assigned(new PersonRolesResponse(person.Id, person.Roles.Select(r => r.Name).ToList()));
    }

    public async Task<RoleRemovalResult> RemoveRoleAsync(
        Guid personId, string roleName, CancellationToken cancellationToken = default)
    {
        var person = await db.People.Include(p => p.Roles).SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return RoleRemovalResult.PersonNotFound($"No Person found with id {personId}.");
        }

        var role = person.Roles.SingleOrDefault(r => r.Name == roleName);
        if (role is null)
        {
            return RoleRemovalResult.Invalid($"Person does not hold the '{roleName}' role.");
        }

        person.Roles.Remove(role);

        // A Practice Lead who loses the role no longer owns any Practice as its
        // lead — clear every Practice pointing at them, not just one, since a
        // Person may lead more than one Practice.
        if (roleName == RoleNames.PracticeLead)
        {
            var ledPractices = await db.Practices.Where(p => p.PracticeLeadId == person.Id).ToListAsync(cancellationToken);
            foreach (var practice in ledPractices)
            {
                practice.PracticeLeadId = null;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return RoleRemovalResult.Removed(new PersonRolesResponse(person.Id, person.Roles.Select(r => r.Name).ToList()));
    }

    // Setting Leaver is available to Admin (any Person) or a Line Manager for
    // their own reports — not a plain role check, so the caller's identity/roles
    // are passed in rather than resolved here.
    public async Task<LeaverTransitionResult> MarkAsLeaverForViewerAsync(
        Guid personId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return LeaverTransitionResult.PersonNotFound($"No Person found with id {personId}.");
        }

        var isLineManagerOfPerson = callerIsLineManager && person.LineManagerId == callerId;
        if (!callerIsAdmin && !isLineManagerOfPerson)
        {
            return LeaverTransitionResult.Forbidden();
        }

        if (person.Status == PersonStatus.Leaver)
        {
            return LeaverTransitionResult.Invalid("Person is already a Leaver.");
        }

        // Cancelling outstanding feedback requests and excluding the Person from
        // future cycle enrolment (spec Section 4) are deferred until the
        // FeedbackRequest entity and cycle engine exist (Milestones 5/6). This
        // transition is also deliberately one-way — there is no "un-leaver"
        // action, per this story's acceptance criteria.
        person.Status = PersonStatus.Leaver;
        person.LeaverSince = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return LeaverTransitionResult.MarkedAsLeaver(ToResponse(person));
    }

    // Backs the Admin Console's People screen (CBLT-306) — the first flat
    // browse view over every Person; every prior read here was either a
    // create/update result or a single-target lookup. Resolves practice and
    // line manager names here so the frontend doesn't need a second round
    // trip per row.
    public async Task<IReadOnlyList<PersonListEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.People
            .Select(p => new PersonListEntry(
                p.Id,
                p.FullName,
                p.Status,
                p.PracticeId,
                p.Practice.Name,
                p.LineManagerId,
                p.LineManager != null ? p.LineManager.FullName : null,
                p.HeadOfPracticeId,
                p.Roles.Select(r => r.Name).ToList(),
                p.Email))
            .ToListAsync(cancellationToken);

    // The scoped single-Person counterpart to GetAllAsync — Admin (any
    // Person), Practice Lead (own practice), or Line Manager (own reports),
    // same three-way check PersonAuthorizationHelpers already centralises for
    // PocService/RequestDispatchService/FeedbackCycleService. Backs the new
    // Person-profile page: PL/LM currently have no way to view a Person at
    // all, since GET /people is Admin-only.
    public async Task<PersonProfileResult> GetForViewerAsync(
        Guid personId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var person = await db.People.SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return PersonProfileResult.PersonNotFound($"No Person found with id {personId}.");
        }

        var authorized = await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
            db, person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken);
        if (!authorized)
        {
            return PersonProfileResult.Forbidden();
        }

        var entry = await db.People
            .Where(p => p.Id == personId)
            .Select(p => new PersonListEntry(
                p.Id,
                p.FullName,
                p.Status,
                p.PracticeId,
                p.Practice.Name,
                p.LineManagerId,
                p.LineManager != null ? p.LineManager.FullName : null,
                p.HeadOfPracticeId,
                p.Roles.Select(r => r.Name).ToList(),
                p.Email))
            .SingleAsync(cancellationToken);

        return PersonProfileResult.Success(entry);
    }

    private static PersonResponse ToResponse(Person person) => new(
        person.Id, person.FullName, person.Status, person.PracticeId, person.LineManagerId, person.HeadOfPracticeId, person.Email);
}
