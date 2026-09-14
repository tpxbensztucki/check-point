using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-235 directly against LmNotificationDispatchService, the same
// style as FeedbackSubmissionServiceTests, since a pending LmNotification needs
// a real submission chain (FeedbackSubmission -> FeedbackRequest ->
// ProjectMembership -> Person, plus a Line Manager) rather than a bare Guid.
public class LmNotificationDispatchServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private Guid _projectId;
    private Guid _practiceId;

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

    private LmNotificationDispatchService CreateService(CheckPointDbContext context, RecordingEmailSender emailSender) =>
        new(context, _time, emailSender, Options.Create(new FrontendOptions()));

    private async Task<Guid> CreateLineManagerAsync(string? email)
    {
        await using var context = CreateContext();
        var manager = new Person { FullName = "Morgan Manager", PracticeId = _practiceId, Email = email };
        context.People.Add(manager);
        await context.SaveChangesAsync();
        return manager.Id;
    }

    // Schedules a New Starter cycle request, adds a POC, issues a link, and
    // submits feedback through it — the real path that queues an
    // LmNotification (FeedbackSubmissionService), rather than inserting the
    // outbox row directly.
    private async Task SubmitFeedbackAsync(Guid personId, string pocName, string pocEmail)
    {
        await using var context = CreateContext();
        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == personId);
        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = pocName,
            Email = pocEmail,
            Relationship = PocRelationship.Internal,
            Role = PocRole.Tech,
        };
        context.Pocs.Add(poc);
        await context.SaveChangesAsync();

        var request = await context.FeedbackRequests
            .SingleAsync(r => r.ProjectMembershipId == membership.Id && r.Stage == FeedbackRequestStage.NewStarterWeek2);

        var link = await new MagicLinkService(context, _time).IssueAsync(request.Id, poc.Id);
        var submissionService = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        await submissionService.SubmitAsync(link.Token, new SubmitFeedbackRequest(
            "Great communication.", "Sometimes misses deadlines.", "Follow up on action items sooner."));
    }

    private async Task<Guid> CreatePersonWithLineManagerAsync(Guid lineManagerId)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Reviewee", PracticeId = _practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var projectService = new ProjectService(context, _time, Options.Create(new NewStarterCycleOptions()));
        await projectService.AddPersonAsync(_projectId, person.Id);

        return person.Id;
    }

    [Fact]
    public async Task APendingNotificationForAnLmWithAnEmail_IsSentAndMarked()
    {
        var lineManagerId = await CreateLineManagerAsync("morgan@example.com");
        var personId = await CreatePersonWithLineManagerAsync(lineManagerId);
        await SubmitFeedbackAsync(personId, "Jamie POC", "jamie@example.com");

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        await CreateService(context, emailSender).DispatchPendingNotificationsAsync();

        Assert.Single(emailSender.Sent);
        Assert.Equal("morgan@example.com", emailSender.Sent.Single().To);

        await using var verify = CreateContext();
        var notification = await verify.LmNotifications.SingleAsync();
        Assert.NotNull(notification.SentAt);
    }

    [Fact]
    public async Task APendingNotificationForAnLmWithNoEmail_IsSkippedAndLeftPending()
    {
        var lineManagerId = await CreateLineManagerAsync(email: null);
        var personId = await CreatePersonWithLineManagerAsync(lineManagerId);
        await SubmitFeedbackAsync(personId, "Jamie POC", "jamie@example.com");

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        await CreateService(context, emailSender).DispatchPendingNotificationsAsync();

        Assert.Empty(emailSender.Sent);

        await using var verify = CreateContext();
        var notification = await verify.LmNotifications.SingleAsync();
        Assert.Null(notification.SentAt);
    }

    [Fact]
    public async Task ThreeSeparateSubmissionsForTheSameLm_EachGetTheirOwnSeparateEmail()
    {
        var lineManagerId = await CreateLineManagerAsync("morgan@example.com");
        var personId = await CreatePersonWithLineManagerAsync(lineManagerId);
        await SubmitFeedbackAsync(personId, "Jamie POC", "jamie@example.com");
        await SubmitFeedbackAsync(personId, "Casey POC", "casey@example.com");
        await SubmitFeedbackAsync(personId, "Alex POC", "alex@example.com");

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        await CreateService(context, emailSender).DispatchPendingNotificationsAsync();

        Assert.Equal(3, emailSender.Sent.Count);
        Assert.All(emailSender.Sent, e => Assert.Equal("morgan@example.com", e.To));

        await using var verify = CreateContext();
        Assert.Equal(3, await verify.LmNotifications.CountAsync(n => n.SentAt != null));
    }

    [Fact]
    public async Task TheNotificationEmail_NeverContainsTheFeedbackContentItself()
    {
        var lineManagerId = await CreateLineManagerAsync("morgan@example.com");
        var personId = await CreatePersonWithLineManagerAsync(lineManagerId);
        await SubmitFeedbackAsync(personId, "Jamie POC", "jamie@example.com");

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        await CreateService(context, emailSender).DispatchPendingNotificationsAsync();

        var body = emailSender.Sent.Single().Body;
        Assert.DoesNotContain("Great communication.", body);
        Assert.DoesNotContain("Sometimes misses deadlines.", body);
        Assert.DoesNotContain("Follow up on action items sooner.", body);
    }

    [Fact]
    public async Task WithNoPendingNotifications_NothingIsSent()
    {
        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        await CreateService(context, emailSender).DispatchPendingNotificationsAsync();

        Assert.Empty(emailSender.Sent);
    }
}
