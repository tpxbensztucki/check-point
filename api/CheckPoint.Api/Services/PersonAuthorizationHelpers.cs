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
}
