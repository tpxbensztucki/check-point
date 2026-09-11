using System.Text.RegularExpressions;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Points of Contact captured against a Person's membership on a Project (spec
// Section 3). Callable by Admin, the Practice Lead of the target Person's
// Practice, or the Line Manager of the target Person — not a plain role check, so
// the caller's identity/roles are passed in rather than resolved here.
public partial class PocService(CheckPointDbContext db)
{
    public async Task<PocAssignmentResult> AssignPocAsync(
        Guid projectId,
        Guid personId,
        CreatePocRequest request,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var membership = await FindActiveMembershipAsync(projectId, personId, cancellationToken);
        if (membership is null)
        {
            return PocAssignmentResult.MembershipNotFound(
                $"No active membership found for Person {personId} on Project {projectId}.");
        }

        if (!await IsAuthorizedAsync(membership, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return PocAssignmentResult.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return PocAssignmentResult.Invalid("Name is required.");
        }

        if (!EmailRegex().IsMatch(request.Email))
        {
            return PocAssignmentResult.Invalid("Email is not a valid email address.");
        }

        db.Pocs.Add(new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = request.Name,
            Email = request.Email,
            Relationship = request.Relationship,
            Role = request.Role,
        });
        await db.SaveChangesAsync(cancellationToken);

        return PocAssignmentResult.Assigned(await BuildResponseAsync(membership.Id, cancellationToken));
    }

    public async Task<PocMutationResult> UpdatePocAsync(
        Guid projectId,
        Guid personId,
        Guid pocId,
        CreatePocRequest request,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var membership = await FindActiveMembershipAsync(projectId, personId, cancellationToken);
        if (membership is null)
        {
            return PocMutationResult.MembershipNotFound(
                $"No active membership found for Person {personId} on Project {projectId}.");
        }

        if (!await IsAuthorizedAsync(membership, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return PocMutationResult.Forbidden();
        }

        var poc = await db.Pocs.SingleOrDefaultAsync(
            p => p.Id == pocId && p.ProjectMembershipId == membership.Id, cancellationToken);
        if (poc is null)
        {
            return PocMutationResult.PocNotFound($"No Poc found with id {pocId} on this membership.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return PocMutationResult.Invalid("Name is required.");
        }

        if (!EmailRegex().IsMatch(request.Email))
        {
            return PocMutationResult.Invalid("Email is not a valid email address.");
        }

        // Prior magic links carry only the FeedbackRequestId, never the POC's
        // email, so correcting it here has no effect on already-sent requests -
        // nothing further to do for that acceptance criterion.
        poc.Name = request.Name;
        poc.Email = request.Email;
        poc.Relationship = request.Relationship;
        poc.Role = request.Role;
        await db.SaveChangesAsync(cancellationToken);

        return PocMutationResult.Success(await BuildResponseAsync(membership.Id, cancellationToken));
    }

    // Cancelling the removed POC's outstanding feedback request (spec Section 3)
    // is deferred until the FeedbackRequest entity exists (Milestone 5).
    public async Task<PocMutationResult> RemovePocAsync(
        Guid projectId,
        Guid personId,
        Guid pocId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var membership = await FindActiveMembershipAsync(projectId, personId, cancellationToken);
        if (membership is null)
        {
            return PocMutationResult.MembershipNotFound(
                $"No active membership found for Person {personId} on Project {projectId}.");
        }

        if (!await IsAuthorizedAsync(membership, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return PocMutationResult.Forbidden();
        }

        var poc = await db.Pocs.SingleOrDefaultAsync(
            p => p.Id == pocId && p.ProjectMembershipId == membership.Id, cancellationToken);
        if (poc is null)
        {
            return PocMutationResult.PocNotFound($"No Poc found with id {pocId} on this membership.");
        }

        db.Pocs.Remove(poc);
        await db.SaveChangesAsync(cancellationToken);

        return PocMutationResult.Success(await BuildResponseAsync(membership.Id, cancellationToken));
    }

    public async Task<PocViewResult> GetPocsForViewerAsync(
        Guid projectId,
        Guid personId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var membership = await FindActiveMembershipAsync(projectId, personId, cancellationToken);
        if (membership is null)
        {
            return PocViewResult.MembershipNotFound(
                $"No active membership found for Person {personId} on Project {projectId}.");
        }

        if (!await IsAuthorizedAsync(membership, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return PocViewResult.Forbidden();
        }

        return PocViewResult.Success(await BuildResponseAsync(membership.Id, cancellationToken));
    }

    private Task<ProjectMembership?> FindActiveMembershipAsync(
        Guid projectId, Guid personId, CancellationToken cancellationToken) =>
        db.ProjectMemberships.SingleOrDefaultAsync(
            m => m.ProjectId == projectId && m.PersonId == personId && m.RemovedAt == null, cancellationToken);

    private async Task<bool> IsAuthorizedAsync(
        ProjectMembership membership,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken)
    {
        if (callerIsAdmin)
        {
            return true;
        }

        var person = await db.People.SingleAsync(p => p.Id == membership.PersonId, cancellationToken);

        if (callerIsLineManager && person.LineManagerId == callerId)
        {
            return true;
        }

        if (callerIsPracticeLead &&
            await db.Practices.AnyAsync(p => p.Id == person.PracticeId && p.PracticeLeadId == callerId, cancellationToken))
        {
            return true;
        }

        return false;
    }

    private async Task<ProjectMembershipPocsResponse> BuildResponseAsync(
        Guid membershipId, CancellationToken cancellationToken)
    {
        var pocs = await db.Pocs
            .Where(p => p.ProjectMembershipId == membershipId)
            .Select(p => new PocResponse(p.Id, p.Name, p.Email, p.Relationship, p.Role))
            .ToListAsync(cancellationToken);

        var presentRoles = pocs.Select(p => p.Role).ToHashSet();
        var missingRoles = Enum.GetValues<PocRole>().Where(r => !presentRoles.Contains(r)).ToList();

        return new ProjectMembershipPocsResponse(membershipId, pocs, missingRoles);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
