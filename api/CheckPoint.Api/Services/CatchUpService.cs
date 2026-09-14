using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Once a CatchUp already exists (created by FeedbackCycleService's flag/ad-hoc
// entry points), everything about its outcome and history lives here instead —
// a distinct enough feature area (spec Section 5.3, CBLT-241/242) to warrant
// its own service, same reasoning as RequestDispatchService/
// PocResponseHistoryService being split off FeedbackCycleService in Milestone 7.
public class CatchUpService(CheckPointDbContext db, TimeProvider timeProvider)
{
    public async Task<RecordOutcomeResult> RecordOutcomeAsync(
        Guid catchUpId,
        CatchUpOutcomeType outcomeType,
        string? notes,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        var catchUp = await db.CatchUps
            .Include(c => c.Person)
            .SingleOrDefaultAsync(c => c.Id == catchUpId, cancellationToken);
        if (catchUp is null)
        {
            return RecordOutcomeResult.CatchUpNotFound($"No CatchUp found with id {catchUpId}.");
        }

        if (!await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
                db, catchUp.Person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return RecordOutcomeResult.Forbidden;
        }

        if (catchUp.Status != CatchUpStatus.Pending)
        {
            return RecordOutcomeResult.AlreadyRecorded;
        }

        // "Or free-text equivalent" (spec AC) — Other only carries meaning
        // paired with actual free text.
        if (outcomeType == CatchUpOutcomeType.Other && string.IsNullOrWhiteSpace(notes))
        {
            return RecordOutcomeResult.Invalid("Notes are required when the outcome type is Other.");
        }

        var now = timeProvider.GetUtcNow();
        catchUp.Status = CatchUpStatus.Recorded;
        catchUp.OutcomeType = outcomeType;
        catchUp.OutcomeNotes = notes;
        catchUp.RecordedAt = now;

        // Cleared unless the outcome itself specifies continued review (spec
        // AC) — escalating further means the review isn't actually over yet.
        if (outcomeType != CatchUpOutcomeType.EscalateFurther)
        {
            catchUp.Person.UnderReviewSince = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        return RecordOutcomeResult.Recorded(CatchUpResponse.From(catchUp));
    }

    // A Person's full flag/ad-hoc-review/catch-up-outcome history in one place
    // (spec Section 5.3, CBLT-242) — same three-way scoping as
    // PocResponseHistoryService.GetPocHistoryAsync. An empty list (no history
    // at all) is a normal, valid Success response, not an error or a distinct
    // status — the caller distinguishes "no history" from "forbidden" by the
    // Status itself, not by an empty Entries list meaning something different.
    public async Task<PersonCatchUpHistoryResult> GetHistoryAsync(
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
            return PersonCatchUpHistoryResult.PersonNotFound($"No Person found with id {personId}.");
        }

        if (!await PersonAuthorizationHelpers.IsAuthorizedForPersonAsync(
                db, person, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken))
        {
            return PersonCatchUpHistoryResult.Forbidden;
        }

        var catchUps = await db.CatchUps
            .Where(c => c.PersonId == personId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        var entries = catchUps.Select(CatchUpResponse.From).ToList();

        return PersonCatchUpHistoryResult.Success(new PersonCatchUpHistoryResponse(personId, person.UnderReviewSince, entries));
    }
}
