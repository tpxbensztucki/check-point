using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Cross-cutting cycle-engine effects that aren't tied to a single Project or
// Person CRUD operation (spec Section 5). Grows alongside the rest of Milestone 5
// (General cycle scheduling, FY-quarter logic, etc.).
public class FeedbackCycleService(CheckPointDbContext db)
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
}
