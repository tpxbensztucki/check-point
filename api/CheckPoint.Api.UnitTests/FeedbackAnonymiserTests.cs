using CheckPoint.Api.Domain;

namespace CheckPoint.Api.UnitTests;

// Pure transform tests for CBLT-246's anonymisation logic — no database, no
// HTTP. See CheckPoint.Api.IntegrationTests for anything that touches the
// export endpoint once it exists (CBLT-247).
public class FeedbackAnonymiserTests
{
    private static IdentifiedFeedbackEntry Entry(
        string name, string email, PocRole role, string doingWell, string notDoingWell, string needsToImprove) =>
        new(name, email, role, doingWell, notDoingWell, needsToImprove);

    [Fact]
    public void Anonymise_ReturnsContentOnly_WithNoIdentityFields()
    {
        var submissions = new[]
        {
            Entry("Jamie Tech", "jamie@example.com", PocRole.Tech, "Great communicator", "Misses deadlines", "Time management"),
            Entry("Dana Dm", "dana@example.com", PocRole.Dm, "Strong delivery", "Quiet in meetings", "Speak up more"),
            Entry("Sam Other", "sam@example.com", PocRole.Other, "Reliable", "Slow to respond", "Faster turnaround"),
        };

        var result = FeedbackAnonymiser.Anonymise(submissions);

        Assert.Equal(3, result.Count);
        // AnonymisedFeedbackEntry has no name/email/role property at all —
        // this loop over its declared properties is a structural guarantee,
        // not just a spot check of values.
        var properties = typeof(AnonymisedFeedbackEntry).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("RespondentName", properties);
        Assert.DoesNotContain("RespondentEmail", properties);
        Assert.DoesNotContain("RespondentRole", properties);
    }

    [Fact]
    public void Anonymise_PreservesEveryEntrysFeedbackContent()
    {
        var submissions = new[]
        {
            Entry("Jamie Tech", "jamie@example.com", PocRole.Tech, "Great communicator", "Misses deadlines", "Time management"),
            Entry("Dana Dm", "dana@example.com", PocRole.Dm, "Strong delivery", "Quiet in meetings", "Speak up more"),
        };

        var result = FeedbackAnonymiser.Anonymise(submissions);

        Assert.Contains(result, e => e is { DoingWell: "Great communicator", NotDoingWell: "Misses deadlines", NeedsToImprove: "Time management" });
        Assert.Contains(result, e => e is { DoingWell: "Strong delivery", NotDoingWell: "Quiet in meetings", NeedsToImprove: "Speak up more" });
    }

    [Fact]
    public void Anonymise_IsDeterministic_GivenTheSameInputTwice()
    {
        var submissions = new[]
        {
            Entry("Jamie Tech", "jamie@example.com", PocRole.Tech, "Great communicator", "Misses deadlines", "Time management"),
            Entry("Dana Dm", "dana@example.com", PocRole.Dm, "Strong delivery", "Quiet in meetings", "Speak up more"),
            Entry("Sam Other", "sam@example.com", PocRole.Other, "Reliable", "Slow to respond", "Faster turnaround"),
        };

        var first = FeedbackAnonymiser.Anonymise(submissions);
        var second = FeedbackAnonymiser.Anonymise(submissions);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Anonymise_OrdersByContent_NotBySubmissionOrder()
    {
        // Deliberately submitted in an order that does NOT match alphabetical
        // DoingWell order, so a passing test proves the output order is
        // derived from content, not preserved from input order (which could
        // otherwise correlate back to submission time / a known POC list).
        var submissions = new[]
        {
            Entry("Zed Tech", "zed@example.com", PocRole.Tech, "Zebra-quality work", "N/A", "N/A"),
            Entry("Amy Dm", "amy@example.com", PocRole.Dm, "Amazing work", "N/A", "N/A"),
            Entry("Mel Other", "mel@example.com", PocRole.Other, "Middling work", "N/A", "N/A"),
        };

        var result = FeedbackAnonymiser.Anonymise(submissions);

        Assert.Equal(
            ["Amazing work", "Middling work", "Zebra-quality work"],
            result.Select(e => e.DoingWell));
    }

    [Fact]
    public void Anonymise_ReordersEvenWhenReshuffledInputArrives()
    {
        // The same three entries, submitted in a different input order than
        // the previous test — the anonymised output order must be identical
        // regardless, since it's derived purely from content.
        var submissions = new[]
        {
            Entry("Mel Other", "mel@example.com", PocRole.Other, "Middling work", "N/A", "N/A"),
            Entry("Zed Tech", "zed@example.com", PocRole.Tech, "Zebra-quality work", "N/A", "N/A"),
            Entry("Amy Dm", "amy@example.com", PocRole.Dm, "Amazing work", "N/A", "N/A"),
        };

        var result = FeedbackAnonymiser.Anonymise(submissions);

        Assert.Equal(
            ["Amazing work", "Middling work", "Zebra-quality work"],
            result.Select(e => e.DoingWell));
    }

    [Fact]
    public void Anonymise_OnAnEmptySet_ReturnsAnEmptyList()
    {
        var result = FeedbackAnonymiser.Anonymise([]);

        Assert.Empty(result);
    }
}
