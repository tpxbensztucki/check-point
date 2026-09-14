using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CheckPoint.Api.Services;

// Cross-cutting cycle-engine effects that aren't tied to a single Project or
// Person CRUD operation (spec Section 5). Grows alongside the rest of Milestone 5.
public class FeedbackCycleService(CheckPointDbContext db, TimeProvider timeProvider, IOptions<GeneralCycleOptions> generalCycleOptions)
{
    // The LM-facing flag action itself (spec Section 5.3, CBLT-239) — the
    // caller-aware wrapper around the pre-existing HandleCheckInFlaggedAsync
    // hook, which has no authorization concept of its own. Deliberately a
    // two-way check (Admin OR the Person's own Line Manager), not the shared
    // three-way PersonAuthorizationHelpers: this ticket's own AC only ever
    // mentions a Line Manager ("A Line Manager cannot flag feedback for a
    // Person who is not their report"), unlike CBLT-240's ad-hoc trigger,
    // which explicitly includes Practice Lead too — same two-way shape as
    // PersonService.MarkAsLeaverForViewerAsync.
    public async Task<FlagResult> FlagCheckInAsync(
        Guid feedbackRequestId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var request = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);
        if (request is null)
        {
            return FlagResult.RequestNotFound($"No FeedbackRequest found with id {feedbackRequestId}.");
        }

        var person = request.ProjectMembership.Person;
        var isLineManagerOfPerson = callerIsLineManager && person.LineManagerId == callerId;
        if (!callerIsAdmin && !isLineManagerOfPerson)
        {
            return FlagResult.Forbidden;
        }

        await HandleCheckInFlaggedAsync(feedbackRequestId, cancellationToken);

        var catchUp = await db.CatchUps.SingleAsync(c => c.FeedbackRequestId == feedbackRequestId, cancellationToken);
        return FlagResult.Flagged(CatchUpResponse.From(catchUp));
    }

    // FY quarters run Apr-Jun / Jul-Sep / Oct-Dec / Jan-Mar (spec Section 5.2), so
    // boundaries fall on the 1st of these calendar months, in year order.
    private static readonly int[] QuarterStartMonths = [1, 4, 7, 10];

    // The single, well-defined entry point for "a check-in's feedback was
    // flagged" (spec Section 5.3, CBLT-230) — callable for any check-in in any
    // cycle. Not wired to any endpoint yet since the unified flag action itself
    // doesn't exist (CBLT-239, Milestone 8); this is the hook it will call once it
    // does. Always sets the Person Under Review and creates a pending catch-up
    // record; only a New Starter 4-week check-in additionally triggers the
    // 6-week insert (CBLT-227). Idempotent per check-in (a CatchUp already
    // existing for this FeedbackRequestId means it's already been processed) —
    // recording who eventually handles the catch-up, and any other flagging
    // effect (e.g. cross-check-in non-response tracking), belongs to the Ad-hoc
    // Review epic and isn't implemented here.
    public async Task HandleCheckInFlaggedAsync(Guid feedbackRequestId, CancellationToken cancellationToken = default)
    {
        var flagged = await db.FeedbackRequests
            .Include(r => r.ProjectMembership).ThenInclude(m => m.Person)
            .SingleOrDefaultAsync(r => r.Id == feedbackRequestId, cancellationToken);

        if (flagged is null)
        {
            return;
        }

        var alreadyProcessed = await db.CatchUps.AnyAsync(c => c.FeedbackRequestId == feedbackRequestId, cancellationToken);
        if (alreadyProcessed)
        {
            return;
        }

        CreateCatchUp(flagged.ProjectMembership.Person, flagged.Id);

        // Scoped specifically to the New Starter cycle's 4-week stage (spec
        // Section 5.1/5.3) — flagging any other stage has no further effect here.
        if (flagged.Stage == FeedbackRequestStage.NewStarterWeek4)
        {
            var alreadyInsertedSixWeek = await db.FeedbackRequests.AnyAsync(
                r => r.ProjectMembershipId == flagged.ProjectMembershipId && r.Stage == FeedbackRequestStage.NewStarterWeek6,
                cancellationToken);
            if (!alreadyInsertedSixWeek)
            {
                // Same mechanism as any other scheduled request — no POC
                // snapshot, no special-cased notification path — so it
                // automatically follows the same POC-targeting and notification
                // behaviour once the dispatch job exists.
                db.FeedbackRequests.Add(new FeedbackRequest
                {
                    ProjectMembershipId = flagged.ProjectMembershipId,
                    ScheduledFor = flagged.ProjectMembership.JoinedAt.AddDays(6 * 7),
                    Stage = FeedbackRequestStage.NewStarterWeek6,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // Lets a Practice Lead or Line Manager start a review at any time,
    // independent of a specific check-in (spec Section 5.3, CBLT-240) — same
    // Under Review + catch-up mechanism as flagging, but never tied to a
    // FeedbackRequest and never triggers the 6-week insert. Three-way auth via
    // the shared helper, unlike FlagCheckInAsync's two-way check, since this
    // ticket's own AC explicitly names both Practice Lead and Line Manager.
    // Guards independently of HandleCheckInFlaggedAsync's own per-check-in
    // guard: a Person can only have one *pending* CatchUp at a time from this
    // path, but that's this method's own rule, not a change to how flagging
    // behaves (flagging two different check-ins for the same Person still
    // creates two CatchUps, exactly as before — see CatchUpHookTests.cs).
    public async Task<AdHocReviewResult> TriggerAdHocReviewAsync(
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
            return AdHocReviewResult.PersonNotFound($"No Person found with id {personId}.");
        }

        if (!await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
                db, person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return AdHocReviewResult.Forbidden;
        }

        var pending = await db.CatchUps.SingleOrDefaultAsync(
            c => c.PersonId == personId && c.Status == CatchUpStatus.Pending, cancellationToken);
        if (pending is not null)
        {
            return AdHocReviewResult.Triggered(CatchUpResponse.From(pending), alreadyPending: true);
        }

        var catchUp = CreateCatchUp(person, feedbackRequestId: null);
        await db.SaveChangesAsync(cancellationToken);

        return AdHocReviewResult.Triggered(CatchUpResponse.From(catchUp), alreadyPending: false);
    }

    private CatchUp CreateCatchUp(Person person, Guid? feedbackRequestId)
    {
        person.UnderReviewSince = timeProvider.GetUtcNow();

        var catchUp = new CatchUp
        {
            PersonId = person.Id,
            FeedbackRequestId = feedbackRequestId,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.CatchUps.Add(catchUp);
        return catchUp;
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
