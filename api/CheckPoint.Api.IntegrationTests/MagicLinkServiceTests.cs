using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

public class MagicLinkServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Guid _pocId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateContext(TimeProvider.System);
        await context.Database.MigrateAsync();

        // PocId is a real FK (CBLT-302) — every link in these tests needs an
        // actual Poc row to point at, even though FeedbackRequestId stays a bare,
        // unconstrained Guid (CBLT-213's deliberate decoupling).
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        context.Practices.Add(practice);
        await context.SaveChangesAsync();

        var person = new Person { FullName = "Riley Reviewee", PracticeId = practice.Id };
        var project = new Project { Name = "Website Revamp" };
        context.People.Add(person);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = project.Id, PersonId = person.Id, JoinedAt = DateTimeOffset.UtcNow };
        context.ProjectMemberships.Add(membership);
        await context.SaveChangesAsync();

        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = "Jamie POC",
            Email = "jamie@example.com",
            Relationship = PocRelationship.Internal,
            Role = PocRole.Tech,
        };
        context.Pocs.Add(poc);
        await context.SaveChangesAsync();
        _pocId = poc.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private CheckPointDbContext CreateContext(TimeProvider timeProvider)
    {
        var options = new DbContextOptionsBuilder<CheckPointDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new CheckPointDbContext(options);
    }

    [Fact]
    public async Task ValidLinkIssuedRecently_GrantsAccessToItsFeedbackRequestOnly()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var feedbackRequestId = Guid.NewGuid();
        string token;
        await using (var context = CreateContext(time))
        {
            var link = await new MagicLinkService(context, time).IssueAsync(feedbackRequestId, _pocId);
            token = link.Token;
        }

        time.Advance(TimeSpan.FromDays(3));
        await using var verifyContext = CreateContext(time);
        var result = await new MagicLinkService(verifyContext, time).ValidateAsync(token);

        Assert.Equal(MagicLinkValidationStatus.Valid, result.Status);
        Assert.Equal(feedbackRequestId, result.FeedbackRequestId);
    }

    [Fact]
    public async Task LinkOlderThanSevenDays_IsRejectedAsExpired()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        string token;
        await using (var context = CreateContext(time))
        {
            var link = await new MagicLinkService(context, time).IssueAsync(Guid.NewGuid(), _pocId);
            token = link.Token;
        }

        time.Advance(TimeSpan.FromDays(8));
        await using var verifyContext = CreateContext(time);
        var result = await new MagicLinkService(verifyContext, time).ValidateAsync(token);

        Assert.Equal(MagicLinkValidationStatus.Expired, result.Status);
        Assert.Null(result.FeedbackRequestId);
    }

    [Fact]
    public async Task AlreadySubmittedLink_CannotBeUsedAgain()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        string token;
        await using (var context = CreateContext(time))
        {
            var link = await new MagicLinkService(context, time).IssueAsync(Guid.NewGuid(), _pocId);
            token = link.Token;
        }

        await using (var submitContext = CreateContext(time))
        {
            var consumeResult = await new MagicLinkService(submitContext, time).ConsumeAsync(token);
            Assert.Equal(MagicLinkValidationStatus.Valid, consumeResult.Status);
        }

        await using var secondAttemptContext = CreateContext(time);
        var secondAttempt = await new MagicLinkService(secondAttemptContext, time).ConsumeAsync(token);

        Assert.Equal(MagicLinkValidationStatus.AlreadyUsed, secondAttempt.Status);
    }

    [Fact]
    public async Task UnknownToken_IsRejectedAsNotFound()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await using var context = CreateContext(time);

        var result = await new MagicLinkService(context, time).ValidateAsync("does-not-exist");

        Assert.Equal(MagicLinkValidationStatus.NotFound, result.Status);
    }
}
