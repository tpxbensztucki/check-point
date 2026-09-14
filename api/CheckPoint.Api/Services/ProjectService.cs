using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Projects and Person<->Project membership (spec Section 3).
public class ProjectService(CheckPointDbContext db, TimeProvider timeProvider, AdminSettingsService adminSettingsService)
{
    private static readonly FeedbackRequestStage[] NewStarterStages =
    [
        FeedbackRequestStage.NewStarterWeek2,
        FeedbackRequestStage.NewStarterWeek4,
        FeedbackRequestStage.NewStarterWeek8,
    ];

    public async Task<ProjectCreationResult> CreateProjectAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ProjectCreationResult.Invalid("Name is required.");
        }

        var project = new Project { Name = name };
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);

        return ProjectCreationResult.Created(new ProjectResponse(project.Id, project.Name, project.Status));
    }

    // Cancels every still-Scheduled FeedbackRequest tied to this Project so none
    // of them fire later (spec Section 5.1), then performs the status transition
    // itself, independent of any Person's Employed/Leaver status or their other
    // Projects.
    public async Task<ProjectCompletionResult> CompleteProjectAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectCompletionResult.ProjectNotFound($"No Project found with id {projectId}.");
        }

        if (project.Status == ProjectStatus.Completed)
        {
            return ProjectCompletionResult.Invalid("Project is already Completed.");
        }

        project.Status = ProjectStatus.Completed;

        var scheduledRequests = await db.FeedbackRequests
            .Where(r => r.ProjectMembership.ProjectId == projectId && r.Status == FeedbackRequestStatus.Scheduled)
            .ToListAsync(cancellationToken);
        foreach (var request in scheduledRequests)
        {
            request.Status = FeedbackRequestStatus.Cancelled;
        }

        await db.SaveChangesAsync(cancellationToken);

        return ProjectCompletionResult.Completed(new ProjectResponse(project.Id, project.Name, project.Status));
    }

    // Visibility follows the same role scoping as the org tree (spec Section 2):
    // Admin sees any Person's Projects, a Practice Lead only their own Practice's
    // People, a Line Manager only their own reports — not a plain role check, so
    // the caller's identity/roles are passed in rather than resolved here.
    public async Task<PersonProjectsResult> GetProjectsForPersonAsync(
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
            return PersonProjectsResult.PersonNotFound($"No Person found with id {personId}.");
        }

        var isLineManagerOfPerson = callerIsLineManager && person.LineManagerId == callerId;
        var isLeadOfPersonsPractice = callerIsPracticeLead &&
            await db.Practices.AnyAsync(p => p.Id == person.PracticeId && p.PracticeLeadId == callerId, cancellationToken);
        if (!callerIsAdmin && !isLineManagerOfPerson && !isLeadOfPersonsPractice)
        {
            return PersonProjectsResult.Forbidden();
        }

        var memberships = await db.ProjectMemberships
            .Where(m => m.PersonId == personId && m.RemovedAt == null)
            .Select(m => new { m.Id, m.ProjectId, m.Project.Name, m.Project.Status })
            .ToListAsync(cancellationToken);

        var membershipIds = memberships.Select(m => m.Id).ToList();
        var rolesByMembership = await db.Pocs
            .Where(p => membershipIds.Contains(p.ProjectMembershipId))
            .GroupBy(p => p.ProjectMembershipId)
            .Select(g => new { MembershipId = g.Key, Roles = g.Select(p => p.Role).ToList() })
            .ToDictionaryAsync(g => g.MembershipId, g => g.Roles, cancellationToken);

        var summaries = memberships
            .Select(m => new PersonProjectSummary(
                m.ProjectId,
                m.Name,
                m.Status,
                m.Status == ProjectStatus.Active
                    ? PocRoleHelpers.ComputeMissingRoles(rolesByMembership.GetValueOrDefault(m.Id, []))
                    : null))
            .OrderBy(s => s.ProjectName)
            .ToList();

        return PersonProjectsResult.Success(summaries);
    }

    public async Task<ProjectMembershipResult> AddPersonAsync(
        Guid projectId, Guid personId, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return ProjectMembershipResult.ProjectNotFound($"No Project found with id {projectId}.");
        }

        var person = await db.People.SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is null)
        {
            return ProjectMembershipResult.Invalid($"No Person found with id {personId}.");
        }

        if (person.Status == PersonStatus.Leaver)
        {
            return ProjectMembershipResult.Invalid("A Leaver cannot be added to a Project.");
        }

        var alreadyActiveMember = await db.ProjectMemberships.AnyAsync(
            m => m.ProjectId == projectId && m.PersonId == personId && m.RemovedAt == null, cancellationToken);
        if (alreadyActiveMember)
        {
            return ProjectMembershipResult.Invalid("Person is already an active member of this Project.");
        }

        var joinedAt = timeProvider.GetUtcNow();
        var membership = new ProjectMembership
        {
            ProjectId = projectId,
            PersonId = personId,
            JoinedAt = joinedAt,
        };
        db.ProjectMemberships.Add(membership);

        // New Starter cycle scheduling (spec Section 5.1): one FeedbackRequest per
        // configured interval, relative to this Person's own start on this
        // Project (not the Project's creation date), so staggered starters get
        // staggered schedules. Which POCs to send to is deliberately not resolved
        // or stored here — the dispatch job (Milestone 7) looks up the Project's
        // currently assigned POCs at send time.
        //
        // IntervalWeeks is assumed to have exactly three entries, one per fixed
        // New Starter stage identity below (2/4/8-week by default) — reconfiguring
        // the shape of this array, not just its values, is a bigger change not
        // covered by CBLT-252 (Admin Settings: interval schedule).
        var settings = await adminSettingsService.GetAsync(cancellationToken);
        var intervalWeeks = settings.NewStarterIntervalWeeks;
        for (var i = 0; i < intervalWeeks.Length && i < NewStarterStages.Length; i++)
        {
            db.FeedbackRequests.Add(new FeedbackRequest
            {
                ProjectMembership = membership,
                ScheduledFor = joinedAt.AddDays(intervalWeeks[i] * 7),
                Stage = NewStarterStages[i],
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return ProjectMembershipResult.Added(
            new ProjectMembershipResponse(membership.Id, membership.ProjectId, membership.PersonId, membership.JoinedAt));
    }

    // Soft delete: RemovedAt is set rather than the row deleted, so the Person's
    // history on this Project (spec Section 3) is preserved.
    public async Task<ProjectMembershipRemovalResult> RemovePersonAsync(
        Guid projectId, Guid personId, CancellationToken cancellationToken = default)
    {
        var projectExists = await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken);
        if (!projectExists)
        {
            return ProjectMembershipRemovalResult.ProjectNotFound($"No Project found with id {projectId}.");
        }

        var membership = await db.ProjectMemberships.SingleOrDefaultAsync(
            m => m.ProjectId == projectId && m.PersonId == personId && m.RemovedAt == null, cancellationToken);
        if (membership is null)
        {
            return ProjectMembershipRemovalResult.MembershipNotFound(
                $"No active membership found for Person {personId} on Project {projectId}.");
        }

        membership.RemovedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return ProjectMembershipRemovalResult.Removed();
    }

    // Backs the Admin Console's Projects screen (CBLT-307) — the first flat
    // browse view over every Project; every prior read here was either a
    // create/complete result or a single-Person's-Projects view.
    public async Task<IReadOnlyList<ProjectResponse>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.Projects
            .Select(p => new ProjectResponse(p.Id, p.Name, p.Status))
            .ToListAsync(cancellationToken);

    // Who is currently (non-removed) on a Project (CBLT-307) — the reverse
    // of GetProjectsForPersonAsync; no such view existed before this, since
    // nothing before the Admin Console needed to browse a Project's people.
    public async Task<IReadOnlyList<ProjectMembershipSummary>> GetMembersAsync(
        Guid projectId, CancellationToken cancellationToken = default) =>
        await db.ProjectMemberships
            .Where(m => m.ProjectId == projectId && m.RemovedAt == null)
            .Select(m => new ProjectMembershipSummary(m.Id, m.PersonId, m.Person.FullName, m.JoinedAt))
            .ToListAsync(cancellationToken);
}
