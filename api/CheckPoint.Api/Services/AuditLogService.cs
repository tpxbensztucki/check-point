using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Immutable audit trail of feedback views/exports (spec Section 11, CBLT-249).
// RecordViewAsync/RecordExportAsync are the only ways a row is ever written —
// there is no update/delete method, satisfying the ticket's own "immutable,
// no update or delete path through normal application access" AC by omission,
// the same way FeedbackSubmission's own immutability is enforced.
//
// RecordViewAsync has no caller yet: no endpoint anywhere currently exposes a
// Person's actual feedback content (DoingWell/NotDoingWell/NeedsToImprove) to
// an internal viewer — the only planned mechanism for seeing content at all is
// the not-yet-built PDF export (CBLT-247), which will call RecordExportAsync
// once it exists. Same "ship the hook, wire it up when the triggering feature
// exists" precedent as CBLT-227/230's cycle-engine hooks in Milestone 5.
public class AuditLogService(CheckPointDbContext db)
{
    public Task RecordViewAsync(Guid viewerId, Guid personId, CancellationToken cancellationToken = default) =>
        RecordAsync(viewerId, personId, AuditAction.View, cancellationToken);

    public Task RecordExportAsync(Guid viewerId, Guid personId, CancellationToken cancellationToken = default) =>
        RecordAsync(viewerId, personId, AuditAction.Export, cancellationToken);

    private async Task RecordAsync(
        Guid viewerId, Guid personId, AuditAction action, CancellationToken cancellationToken)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            ViewerId = viewerId,
            PersonId = personId,
            Action = action,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    // Admin-only (spec's own AC — enforced at the endpoint, not here, matching
    // every other plain-role-check group in this codebase). Every filter is
    // optional and combines with AND; an empty result is a normal Success, the
    // same "no matches isn't an error" precedent as every other history view
    // in this codebase (e.g. PocResponseHistoryService).
    public async Task<IReadOnlyList<AuditLogEntryResponse>> GetLogAsync(
        Guid? personId,
        Guid? viewerId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var query = db.AuditLogEntries.AsQueryable();

        if (personId is { } pid)
        {
            query = query.Where(e => e.PersonId == pid);
        }

        if (viewerId is { } vid)
        {
            query = query.Where(e => e.ViewerId == vid);
        }

        if (from is { } fromDate)
        {
            query = query.Where(e => e.OccurredAt >= fromDate);
        }

        if (to is { } toDate)
        {
            query = query.Where(e => e.OccurredAt <= toDate);
        }

        return await query
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new AuditLogEntryResponse(
                e.Id, e.ViewerId, e.Viewer.FullName, e.PersonId, e.Person.FullName, e.Action, e.OccurredAt))
            .ToListAsync(cancellationToken);
    }
}
