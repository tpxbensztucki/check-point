namespace CheckPoint.Api.Domain;

// A respondent's full answer set for one FeedbackSubmission, still carrying
// identity — the input side of the anonymisation transform (spec Section 10,
// CBLT-246). Deliberately not the FeedbackSubmission entity itself, so this
// transform has no dependency on EF Core or the database and can be
// exercised as a pure unit test.
public record IdentifiedFeedbackEntry(
    string RespondentName,
    string RespondentEmail,
    PocRole RespondentRole,
    string DoingWell,
    string NotDoingWell,
    string NeedsToImprove);

// The anonymised result — content only, no respondent name/email/role field
// exists on this type at all, so there is nothing for a caller to
// accidentally leak.
public record AnonymisedFeedbackEntry(string DoingWell, string NotDoingWell, string NeedsToImprove);

// Pure, stateless transform — reusable by any future anonymised view (PDF
// export, CBLT-247, or otherwise), not just one specific consumer.
public static class FeedbackAnonymiser
{
    public static IReadOnlyList<AnonymisedFeedbackEntry> Anonymise(
        IEnumerable<IdentifiedFeedbackEntry> submissions) =>
        submissions
            .Select(s => new AnonymisedFeedbackEntry(s.DoingWell, s.NotDoingWell, s.NeedsToImprove))
            // Ordered by content itself, never by submission time or POC
            // list order — both of those would let someone with outside
            // knowledge of who submitted when correlate an anonymised entry
            // back to a respondent. Sorting on the content guarantees the
            // same set of answers always anonymises to the same order
            // regardless of the order submissions happened to arrive in,
            // satisfying determinism at the same time. Three-field ordinal
            // comparison keeps the result fully deterministic even when two
            // entries share identical DoingWell/NotDoingWell text.
            .OrderBy(e => e.DoingWell, StringComparer.Ordinal)
            .ThenBy(e => e.NotDoingWell, StringComparer.Ordinal)
            .ThenBy(e => e.NeedsToImprove, StringComparer.Ordinal)
            .ToList();
}
