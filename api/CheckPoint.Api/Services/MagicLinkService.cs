using System.Security.Cryptography;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// The magic-link mechanism itself (spec Section 9) — generation and verification,
// independent of the guest-facing form that consumes it (Guest Feedback Form epic).
public class MagicLinkService(CheckPointDbContext db, TimeProvider timeProvider)
{
    public static readonly TimeSpan ValidityPeriod = TimeSpan.FromDays(7);

    // 256 bits of entropy, base64url-encoded so it's safe to put directly in a URL
    // and cannot feasibly be guessed or enumerated. Internal (not private) so its
    // properties can be unit-tested directly without a database.
    internal static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public async Task<MagicLink> IssueAsync(Guid feedbackRequestId, Guid pocId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var link = new MagicLink
        {
            Token = GenerateToken(),
            FeedbackRequestId = feedbackRequestId,
            PocId = pocId,
            IssuedAt = now,
            ExpiresAt = now + ValidityPeriod,
        };

        db.MagicLinks.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    // Read-only check, used when the guest opens the link — does not consume it, so
    // they can revisit the form before submitting.
    public async Task<MagicLinkValidationResult> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        var (link, status) = await LoadAndCheckAsync(token, cancellationToken);
        return status == MagicLinkValidationStatus.Valid
            ? MagicLinkValidationResult.Valid(link!.FeedbackRequestId)
            : new MagicLinkValidationResult(status, null);
    }

    // Marks the link used, so it cannot be used again to resubmit. Only succeeds if
    // the link is currently valid; call ValidateAsync first to check outright.
    public async Task<MagicLinkValidationResult> ConsumeAsync(string token, CancellationToken cancellationToken = default)
    {
        var (link, status) = await LoadAndCheckAsync(token, cancellationToken);
        if (status != MagicLinkValidationStatus.Valid)
        {
            return new MagicLinkValidationResult(status, null);
        }

        link!.UsedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return MagicLinkValidationResult.Valid(link.FeedbackRequestId);
    }

    // Internal (not private) so FeedbackSubmissionService can validate a token and
    // then mutate the same tracked MagicLink entity as part of its own single
    // SaveChangesAsync call, instead of calling ConsumeAsync separately and
    // burning the link before the submitted content has even been validated.
    internal async Task<(MagicLink? Link, MagicLinkValidationStatus Status)> LoadAndCheckAsync(
        string token, CancellationToken cancellationToken)
    {
        var link = await db.MagicLinks.SingleOrDefaultAsync(l => l.Token == token, cancellationToken);
        if (link is null)
        {
            return (null, MagicLinkValidationStatus.NotFound);
        }

        if (link.InvalidatedAt is not null)
        {
            return (link, MagicLinkValidationStatus.Superseded);
        }

        if (link.UsedAt is not null)
        {
            return (link, MagicLinkValidationStatus.AlreadyUsed);
        }

        if (timeProvider.GetUtcNow() > link.ExpiresAt)
        {
            return (link, MagicLinkValidationStatus.Expired);
        }

        return (link, MagicLinkValidationStatus.Valid);
    }
}
