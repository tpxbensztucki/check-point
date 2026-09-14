using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Extracted from PocService/RequestDispatchService (CBLT-238) once the same
// "Admin, or the target Person's own Line Manager, or the target Person's
// Practice's Lead" check (spec Section 8) was needed a third time.
public static class PersonAuthorizationHelpers
{
    public static async Task<bool> IsAuthorizedForPersonAsync(
        CheckPointDbContext db,
        Person person,
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

    // The union-of-visible-Person-ids shape used wherever a *list* of People
    // (or things attached to them) needs role-scoped filtering rather than a
    // single-target pass/fail gate — this is its third occurrence
    // (OrgTreeService, PocResponseHistoryService.GetProjectPocPatternsAsync,
    // and now DashboardService, CBLT-243), the threshold this codebase uses
    // for extracting a shared helper (see PocRoleHelpers). Returns null for
    // an Admin, meaning "no filter — sees everyone," to distinguish it from
    // an empty set (a Practice Lead or Line Manager who currently has no one
    // visible at all).
    public static async Task<HashSet<Guid>?> GetVisiblePersonIdsAsync(
        CheckPointDbContext db,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken)
    {
        if (callerIsAdmin)
        {
            return null;
        }

        var visibleIds = new HashSet<Guid>();

        if (callerIsPracticeLead)
        {
            var ledPracticeIds = await db.Practices
                .Where(p => p.PracticeLeadId == callerId)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);

            if (ledPracticeIds.Count > 0)
            {
                var practicePeopleIds = await db.People
                    .Where(p => ledPracticeIds.Contains(p.PracticeId))
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken);
                visibleIds.UnionWith(practicePeopleIds);
            }
        }

        if (callerIsLineManager)
        {
            visibleIds.Add(callerId);
            var reportIds = await db.People
                .Where(p => p.LineManagerId == callerId)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            visibleIds.UnionWith(reportIds);
        }

        return visibleIds;
    }
}
