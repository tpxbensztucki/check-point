using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Cross-cutting cycle-engine effects that aren't tied to a single Project or
// Person CRUD operation (spec Section 5). Grows alongside the rest of Milestone 5
// (General cycle scheduling, FY-quarter logic, etc.).
public class FeedbackCycleService(CheckPointDbContext db, TimeProvider timeProvider)
{
    // Called by the unified flag action's cycle-engine hook (CBLT-230) whenever a
    // check-in's feedback is flagged. Not wired to any endpoint yet since the flag
    // action itself doesn't exist (CBLT-239, Milestone 8) — this is the hook CBLT-
    // 230 will call once it does. Every effect of flagging other than this one
    // (Under Review status, LM/Practice Lead catch-up) belongs to the Ad-hoc
    // Review epic and isn't implemented here.
    public async Task HandleCheckInFlaggedAsync(Guid feedbackRequestId, CancellationToken cancellationToken = default)
    {
        var flagged = await db.FeedbackRequests.SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);

        // Scoped specifically to the New Starter cycle's 4-week stage (spec
        // Section 5.1/5.3) — flagging any other stage has no cycle-engine effect
        // here.
        if (flagged is null || flagged.Stage != FeedbackRequestStage.NewStarterWeek4)
        {
            return;
        }

        var alreadyInserted = await db.FeedbackRequests.AnyAsync(
            r => r.ProjectMembershipId == flagged.ProjectMembershipId && r.Stage == FeedbackRequestStage.NewStarterWeek6,
            cancellationToken);
        if (alreadyInserted)
        {
            return;
        }

        var membership = await db.ProjectMemberships.SingleAsync(
            m => m.Id == flagged.ProjectMembershipId, cancellationToken);

        // Same mechanism as any other scheduled request — no POC snapshot, no
        // special-cased notification path — so it automatically follows the same
        // POC-targeting and notification behaviour once the dispatch job exists.
        db.FeedbackRequests.Add(new FeedbackRequest
        {
            ProjectMembershipId = flagged.ProjectMembershipId,
            ScheduledFor = membership.JoinedAt.AddDays(6 * 7),
            Stage = FeedbackRequestStage.NewStarterWeek6,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    // Called whenever a FeedbackRequest's lifecycle concludes for any reason — a
    // guest submitting it (Milestone 6) or it reaching its No Response expiry
    // (CBLT-237, Milestone 7). Neither trigger exists yet, so this is a hook ready
    // for them to call, not wired to anything itself. NewStarterWeek8 is always
    // the last New Starter stage chronologically, whether or not a Week6 was ever
    // inserted (spec Section 5.1/5.2), so that's the one that triggers enrolment.
    public async Task HandleFeedbackRequestCompletedAsync(
        Guid feedbackRequestId, CancellationToken cancellationToken = default)
    {
        var request = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Project)
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);

        if (request is null || request.Stage != FeedbackRequestStage.NewStarterWeek8)
        {
            return;
        }

        var membership = request.ProjectMembership;
        if (membership.GeneralCycleEnrolledAt is not null)
        {
            return;
        }

        // Per Person per Project: this only ever inspects the one membership tied
        // to the completed request, so a Person's other Projects are untouched.
        if (membership.Project.Status == ProjectStatus.Completed || membership.Person.Status == PersonStatus.Leaver)
        {
            return;
        }

        membership.GeneralCycleEnrolledAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }
}
