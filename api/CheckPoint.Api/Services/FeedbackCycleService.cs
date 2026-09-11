using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CheckPoint.Api.Services;

// Cross-cutting cycle-engine effects that aren't tied to a single Project or
// Person CRUD operation (spec Section 5). Grows alongside the rest of Milestone 5.
public class FeedbackCycleService(CheckPointDbContext db, TimeProvider timeProvider, IOptions<GeneralCycleOptions> generalCycleOptions)
{
    // FY quarters run Apr-Jun / Jul-Sep / Oct-Dec / Jan-Mar (spec Section 5.2), so
    // boundaries fall on the 1st of these calendar months, in year order.
    private static readonly int[] QuarterStartMonths = [1, 4, 7, 10];

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
    // for them to call, not wired to anything itself. Dispatches by Stage: the
    // final New Starter request (CBLT-228) enrols into the General cycle and
    // schedules its first request; a General request (CBLT-229) schedules the
    // next FY-quarter one. Every other stage has no effect here.
    public async Task HandleFeedbackRequestCompletedAsync(
        Guid feedbackRequestId, CancellationToken cancellationToken = default)
    {
        var request = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Project)
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);

        if (request is null)
        {
            return;
        }

        switch (request.Stage)
        {
            case FeedbackRequestStage.NewStarterWeek8:
                await EnrolIntoGeneralCycleAsync(request, cancellationToken);
                break;
            case FeedbackRequestStage.General:
                await ScheduleNextGeneralCycleRequestAsync(request, cancellationToken);
                break;
        }
    }

    // NewStarterWeek8 is always the last New Starter stage chronologically,
    // whether or not a Week6 was ever inserted (spec Section 5.1/5.2).
    private async Task EnrolIntoGeneralCycleAsync(FeedbackRequest request, CancellationToken cancellationToken)
    {
        var membership = request.ProjectMembership;

        // Per Person per Project: this only ever inspects the one membership tied
        // to the completed request, so a Person's other Projects are untouched.
        if (membership.GeneralCycleEnrolledAt is not null)
        {
            return;
        }

        if (membership.Project.Status == ProjectStatus.Completed || membership.Person.Status == PersonStatus.Leaver)
        {
            return;
        }

        var enrolledAt = timeProvider.GetUtcNow();
        membership.GeneralCycleEnrolledAt = enrolledAt;

        // Skip the immediately-next quarter boundary if it's too close to bother
        // scheduling a first request for (spec Section 5.2) — go straight to the
        // one after instead.
        var skipThreshold = TimeSpan.FromDays(generalCycleOptions.Value.SkipThresholdWeeks * 7);
        var nextBoundary = NextQuarterBoundaryOnOrAfter(enrolledAt);
        if (nextBoundary - enrolledAt < skipThreshold)
        {
            nextBoundary = nextBoundary.AddMonths(3);
        }

        db.FeedbackRequests.Add(new FeedbackRequest
        {
            ProjectMembershipId = membership.Id,
            ScheduledFor = nextBoundary,
            Stage = FeedbackRequestStage.General,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    // Continues quarterly for as long as the Project stays Active and the Person
    // stays Employed (spec Section 5.2) — anchored to the completed request's own
    // ScheduledFor (always the 1st of a quarter-start month), not to "now", so the
    // cadence never drifts based on when a request happens to be processed.
    private async Task ScheduleNextGeneralCycleRequestAsync(FeedbackRequest request, CancellationToken cancellationToken)
    {
        var membership = request.ProjectMembership;
        if (membership.Project.Status == ProjectStatus.Completed || membership.Person.Status == PersonStatus.Leaver)
        {
            return;
        }

        var alreadyScheduledNext = await db.FeedbackRequests.AnyAsync(
            r => r.ProjectMembershipId == membership.Id
                && r.Stage == FeedbackRequestStage.General
                && r.ScheduledFor > request.ScheduledFor,
            cancellationToken);
        if (alreadyScheduledNext)
        {
            return;
        }

        db.FeedbackRequests.Add(new FeedbackRequest
        {
            ProjectMembershipId = membership.Id,
            ScheduledFor = request.ScheduledFor.AddMonths(3),
            Stage = FeedbackRequestStage.General,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static DateTimeOffset NextQuarterBoundaryOnOrAfter(DateTimeOffset from)
    {
        foreach (var month in QuarterStartMonths)
        {
            var candidate = new DateTimeOffset(from.Year, month, 1, 0, 0, 0, TimeSpan.Zero);
            if (candidate >= from)
            {
                return candidate;
            }
        }

        return new DateTimeOffset(from.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
