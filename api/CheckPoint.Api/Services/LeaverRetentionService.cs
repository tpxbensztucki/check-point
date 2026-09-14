using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Permanently removes a Leaver's feedback content and respondent identity
// data 6 months after their Leaver status was set (spec Section 11,
// CBLT-248). Person.LeaverSince is the strict anchor — someone who became a
// Leaver 3 months ago is untouched no matter how long the job has existed,
// and a real hard delete (not a soft-delete flag) is used throughout, so
// nothing recoverable survives through normal application access.
public class LeaverRetentionService(CheckPointDbContext db, TimeProvider timeProvider)
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(6 * 30);

    // Returns the ids of every Person actually purged this run — useful for
    // tests and for a future admin-visible run log, though nothing currently
    // consumes it beyond that.
    public async Task<IReadOnlyList<Guid>> PurgeExpiredLeaversAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow() - RetentionPeriod;

        var expiredLeaverIds = await db.People
            .Where(p => p.Status == PersonStatus.Leaver && p.LeaverSince != null && p.LeaverSince <= cutoff)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var personId in expiredLeaverIds)
        {
            await PurgeOnePersonAsync(personId, cancellationToken);
        }

        return expiredLeaverIds;
    }

    // Only feedback-related personal data is touched — the Person row itself,
    // their ProjectMemberships, and every FeedbackRequest's own scheduling
    // metadata (no PII) are left alone, satisfying this ticket's own "org
    // history is unaffected" AC.
    private async Task PurgeOnePersonAsync(Guid personId, CancellationToken cancellationToken)
    {
        var membershipIds = await db.ProjectMemberships
            .Where(m => m.PersonId == personId)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        var pocIds = await db.Pocs
            .Where(p => membershipIds.Contains(p.ProjectMembershipId))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var requestIds = await db.FeedbackRequests
            .Where(r => membershipIds.Contains(r.ProjectMembershipId))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        // MagicLink.PocId is a Restrict FK to Poc — these must go before the
        // Poc rows they reference, or the delete below would be rejected.
        // A guest's one-time token is meaningless once the Poc it was issued
        // to is being purged, so this is a real delete, not an oversight.
        var magicLinks = await db.MagicLinks
            .Where(l => pocIds.Contains(l.PocId))
            .ToListAsync(cancellationToken);
        db.MagicLinks.RemoveRange(magicLinks);

        // The feedback content itself. Deleting these cascades to any
        // LmNotification outbox row that referenced them (configured
        // cascade in CheckPointDbContext), so that outbox never outlives the
        // submission it was about.
        var submissions = await db.FeedbackSubmissions
            .Where(s => requestIds.Contains(s.FeedbackRequestId) || pocIds.Contains(s.PocId))
            .ToListAsync(cancellationToken);
        db.FeedbackSubmissions.RemoveRange(submissions);

        // Respondent identity — the guest's captured name/email/relationship.
        var pocs = await db.Pocs
            .Where(p => membershipIds.Contains(p.ProjectMembershipId))
            .ToListAsync(cancellationToken);
        db.Pocs.RemoveRange(pocs);

        await db.SaveChangesAsync(cancellationToken);
    }
}
