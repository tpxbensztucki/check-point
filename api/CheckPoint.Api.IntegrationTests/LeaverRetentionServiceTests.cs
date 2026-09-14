using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-248's retention job directly against LeaverRetentionService,
// the same style as ProjectServiceSchedulingTests/GeneralCycleSchedulingTests
// — there's no HTTP endpoint, since this is a background job with no user
// trigger, and it needs FakeTimeProvider control over exactly how far past a
// Person's LeaverSince "now" is.
public class LeaverRetentionServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Guid _practiceId;
    private Guid _projectId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        context.Practices.Add(practice);
        var project = new Project { Name = "Website Revamp" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        _practiceId = practice.Id;
        _projectId = project.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private CheckPointDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CheckPointDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new CheckPointDbContext(options);
    }

    // Builds a full Person -> ProjectMembership -> FeedbackRequest -> Poc ->
    // FeedbackSubmission -> MagicLink chain, the same shape real usage
    // produces, so the purge exercises every entity CBLT-248 needs to touch.
    private async Task<(Guid PersonId, Guid SubmissionId, Guid PocId, Guid MagicLinkId)> SeedLeaverWithFeedbackAsync(
        DateTimeOffset? leaverSince)
    {
        await using var context = CreateContext();

        var person = new Person
        {
            FullName = "Riley Leaver",
            PracticeId = _practiceId,
            Status = leaverSince is null ? PersonStatus.Employed : PersonStatus.Leaver,
            LeaverSince = leaverSince,
        };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = _projectId, PersonId = person.Id, JoinedAt = DateTimeOffset.UtcNow };
        context.ProjectMemberships.Add(membership);
        await context.SaveChangesAsync();

        var request = new FeedbackRequest
        {
            ProjectMembershipId = membership.Id,
            ScheduledFor = DateTimeOffset.UtcNow,
            Stage = FeedbackRequestStage.General,
            Status = FeedbackRequestStatus.Sent,
        };
        context.FeedbackRequests.Add(request);

        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = "Jamie Tech",
            Email = "jamie@example.com",
            Relationship = PocRelationship.Client,
            Role = PocRole.Tech,
        };
        context.Pocs.Add(poc);
        await context.SaveChangesAsync();

        var submission = new FeedbackSubmission
        {
            FeedbackRequestId = request.Id,
            PocId = poc.Id,
            DoingWell = "Great work",
            NotDoingWell = "Nothing much",
            NeedsToImprove = "Keep it up",
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        context.FeedbackSubmissions.Add(submission);

        var magicLink = new MagicLink
        {
            Token = Guid.NewGuid().ToString("N"),
            FeedbackRequestId = request.Id,
            PocId = poc.Id,
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            UsedAt = DateTimeOffset.UtcNow,
        };
        context.MagicLinks.Add(magicLink);
        await context.SaveChangesAsync();

        return (person.Id, submission.Id, poc.Id, magicLink.Id);
    }

    [Fact]
    public async Task ALeaverPastSixMonthsAndOneDay_HasTheirFeedbackContentAndRespondentIdentityPermanentlyRemoved()
    {
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var leaverSince = now - TimeSpan.FromDays((6 * 30) + 1);
        var (personId, submissionId, pocId, magicLinkId) = await SeedLeaverWithFeedbackAsync(leaverSince);

        await using (var context = CreateContext())
        {
            var service = new LeaverRetentionService(context, new FakeTimeProvider(now));
            var purgedIds = await service.PurgeExpiredLeaversAsync();
            Assert.Contains(personId, purgedIds);
        }

        await using var verify = CreateContext();
        Assert.False(await verify.FeedbackSubmissions.AnyAsync(s => s.Id == submissionId));
        Assert.False(await verify.Pocs.AnyAsync(p => p.Id == pocId));
        Assert.False(await verify.MagicLinks.AnyAsync(l => l.Id == magicLinkId));
    }

    [Fact]
    public async Task ALeaverThreeMonthsIn_KeepsTheirFeedbackFullyIntact()
    {
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var leaverSince = now - TimeSpan.FromDays(3 * 30);
        var (personId, submissionId, pocId, magicLinkId) = await SeedLeaverWithFeedbackAsync(leaverSince);

        await using (var context = CreateContext())
        {
            var service = new LeaverRetentionService(context, new FakeTimeProvider(now));
            var purgedIds = await service.PurgeExpiredLeaversAsync();
            Assert.DoesNotContain(personId, purgedIds);
        }

        await using var verify = CreateContext();
        Assert.True(await verify.FeedbackSubmissions.AnyAsync(s => s.Id == submissionId));
        Assert.True(await verify.Pocs.AnyAsync(p => p.Id == pocId));
        Assert.True(await verify.MagicLinks.AnyAsync(l => l.Id == magicLinkId));
    }

    [Fact]
    public async Task AfterPurging_ThePersonsOwnOrgDataRemainsFullyVisible()
    {
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var leaverSince = now - TimeSpan.FromDays((6 * 30) + 1);
        var (personId, _, _, _) = await SeedLeaverWithFeedbackAsync(leaverSince);

        await using (var context = CreateContext())
        {
            var service = new LeaverRetentionService(context, new FakeTimeProvider(now));
            await service.PurgeExpiredLeaversAsync();
        }

        await using var verify = CreateContext();
        var person = await verify.People.SingleAsync(p => p.Id == personId);
        Assert.Equal("Riley Leaver", person.FullName);
        Assert.Equal(_practiceId, person.PracticeId);
        Assert.Equal(PersonStatus.Leaver, person.Status);

        // The membership itself (Project participation history) is org data,
        // not feedback data — it survives the purge, unlike the Poc/
        // FeedbackSubmission rows scoped to it.
        Assert.True(await verify.ProjectMemberships.AnyAsync(m => m.PersonId == personId));
    }

    [Fact]
    public async Task AnEmployedPerson_IsNeverPurgedRegardlessOfTime()
    {
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var (personId, submissionId, _, _) = await SeedLeaverWithFeedbackAsync(leaverSince: null);

        await using (var context = CreateContext())
        {
            var service = new LeaverRetentionService(context, new FakeTimeProvider(now));
            var purgedIds = await service.PurgeExpiredLeaversAsync();
            Assert.DoesNotContain(personId, purgedIds);
        }

        await using var verify = CreateContext();
        Assert.True(await verify.FeedbackSubmissions.AnyAsync(s => s.Id == submissionId));
    }

    [Fact]
    public async Task PurgingOnePerson_DoesNotAffectAnotherPersonsFeedback()
    {
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var expiredLeaverSince = now - TimeSpan.FromDays((6 * 30) + 1);
        var (expiredPersonId, expiredSubmissionId, _, _) = await SeedLeaverWithFeedbackAsync(expiredLeaverSince);
        var (stillEmployedPersonId, stillEmployedSubmissionId, _, _) = await SeedLeaverWithFeedbackAsync(leaverSince: null);

        await using (var context = CreateContext())
        {
            var service = new LeaverRetentionService(context, new FakeTimeProvider(now));
            await service.PurgeExpiredLeaversAsync();
        }

        await using var verify = CreateContext();
        Assert.False(await verify.FeedbackSubmissions.AnyAsync(s => s.Id == expiredSubmissionId));
        Assert.True(await verify.FeedbackSubmissions.AnyAsync(s => s.Id == stillEmployedSubmissionId));
        Assert.NotEqual(expiredPersonId, stillEmployedPersonId);
    }
}
