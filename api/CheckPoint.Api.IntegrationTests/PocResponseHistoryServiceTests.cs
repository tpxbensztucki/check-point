using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-238 directly against PocResponseHistoryService, the same
// style as RequestDispatchServiceTests, since a cross-request history needs
// several real FeedbackRequests sharing one ProjectMembership/Poc rather than
// a bare Guid.
public class PocResponseHistoryServiceTests : IAsyncLifetime
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

    private PocResponseHistoryService CreateHistoryService(CheckPointDbContext context) => new(context, _time);

    private RequestDispatchService CreateDispatchService(CheckPointDbContext context, RecordingEmailSender emailSender) =>
        new(
            context,
            _time,
            emailSender,
            new MagicLinkService(context, _time),
            new AdminSettingsService(context),
            Options.Create(new FrontendOptions()));

    // Schedules a New Starter cycle (person joins, gets Week2/4/8 requests
    // scheduled) with one Poc assigned from the start.
    private async Task<(Guid PersonId, Guid PocId)> CreatePersonWithPocAsync(Guid? lineManagerId = null)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Newstarter", PracticeId = _practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var projectService = new ProjectService(context, _time, new AdminSettingsService(context));
        await projectService.AddPersonAsync(_projectId, person.Id);

        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == person.Id);
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

        return (person.Id, poc.Id);
    }

    private async Task<Guid> GetRequestIdAsync(Guid personId, FeedbackRequestStage stage)
    {
        await using var context = CreateContext();
        var request = await context.FeedbackRequests.SingleAsync(
            r => r.ProjectMembership.ProjectId == _projectId && r.ProjectMembership.PersonId == personId && r.Stage == stage);
        return request.Id;
    }

    private async Task DispatchAsync(Guid requestId)
    {
        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateDispatchService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        Assert.Equal(RequestDispatchStatus.Dispatched, result.Status);
    }

    private static string ExtractToken(SentEmail email) =>
        email.Body.Split("/feedback/")[1].Split('\n')[0].Trim();

    private async Task SubmitViaRequestAsync(Guid requestId, Guid pocId)
    {
        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();

        // Re-issue a link for this poc/request since DispatchManuallyAsync
        // already consumed the one issued at dispatch time into an email we
        // don't have a handle on here — simplest is to look up the current
        // non-invalidated link's token isn't exposed, so submit via a freshly
        // issued one instead (a second link for the same poc/request is fine
        // for test purposes; only one is ever "current").
        var link = await new MagicLinkService(context, _time).IssueAsync(requestId, pocId);
        var submissionService = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        var result = await submissionService.SubmitAsync(link.Token, new SubmitFeedbackRequest(
            "Great communication.", "Sometimes misses deadlines.", "Follow up on action items sooner."));
        Assert.Equal(FeedbackSubmissionStatus.Submitted, result.Status);
    }

    [Fact]
    public async Task ThreeConsecutiveNoResponses_AreCountedAsAStreak()
    {
        var (personId, pocId) = await CreatePersonWithPocAsync();
        var week2 = await GetRequestIdAsync(personId, FeedbackRequestStage.NewStarterWeek2);
        var week4 = await GetRequestIdAsync(personId, FeedbackRequestStage.NewStarterWeek4);
        var week8 = await GetRequestIdAsync(personId, FeedbackRequestStage.NewStarterWeek8);

        await DispatchAsync(week2);
        await DispatchAsync(week4);
        await DispatchAsync(week8);
        _time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));

        await using var context = CreateContext();
        var result = await CreateHistoryService(context).GetPocHistoryAsync(
            pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PocHistoryStatus.Success, result.Status);
        Assert.Equal(3, result.History!.ConsecutiveNoResponseCount);
        Assert.Equal(3, result.History.TotalNoResponseCount);
        Assert.Equal(3, result.History.Entries.Count);
        Assert.All(result.History.Entries, e => Assert.Equal(PocResponseStatus.NoResponse, e.Status));
    }

    [Fact]
    public async Task ASubmittedRequest_BreaksTheStreak()
    {
        var (personId, pocId) = await CreatePersonWithPocAsync();
        var week2 = await GetRequestIdAsync(personId, FeedbackRequestStage.NewStarterWeek2);
        var week4 = await GetRequestIdAsync(personId, FeedbackRequestStage.NewStarterWeek4);

        await DispatchAsync(week2);
        await SubmitViaRequestAsync(week2, pocId);
        await DispatchAsync(week4);
        _time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));

        await using var context = CreateContext();
        var result = await CreateHistoryService(context).GetPocHistoryAsync(
            pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        // Most recent (week4) is NoResponse, but the streak stops at week2
        // (Submitted), so it's 1, not 2.
        Assert.Equal(1, result.History!.ConsecutiveNoResponseCount);
        Assert.Equal(1, result.History.TotalNoResponseCount);
        Assert.Equal(2, result.History.Entries.Count);
    }

    [Fact]
    public async Task ARequestThePocWasNeverDispatchedTo_IsExcludedFromHistory()
    {
        var (personId, _) = await CreatePersonWithPocAsync();
        var week2 = await GetRequestIdAsync(personId, FeedbackRequestStage.NewStarterWeek2);
        await DispatchAsync(week2);

        // A second POC added after dispatch never received a link for week2.
        await using var context = CreateContext();
        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == personId);
        var latecomer = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = "Casey Latecomer",
            Email = "casey@example.com",
            Relationship = PocRelationship.Internal,
            Role = PocRole.Dm,
        };
        context.Pocs.Add(latecomer);
        await context.SaveChangesAsync();

        var result = await CreateHistoryService(context).GetPocHistoryAsync(
            latecomer.Id, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Empty(result.History!.Entries);
        Assert.Equal(0, result.History.ConsecutiveNoResponseCount);
    }

    [Fact]
    public async Task AnUnrelatedLineManager_CannotViewAPocsHistory()
    {
        var (personId, pocId) = await CreatePersonWithPocAsync();

        await using var context = CreateContext();
        var result = await CreateHistoryService(context).GetPocHistoryAsync(
            pocId, Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(PocHistoryStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task AnUnknownPocId_ReturnsPocNotFound()
    {
        await using var context = CreateContext();
        var result = await CreateHistoryService(context).GetPocHistoryAsync(
            Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PocHistoryStatus.PocNotFound, result.Status);
    }

    [Fact]
    public async Task AProjectWithTwoPocs_ShowsBothPatternsSideBySide()
    {
        var (goodPersonId, goodPocId) = await CreatePersonWithPocAsync();
        var (poorPersonId, poorPocId) = await CreatePersonWithPocAsync();

        var goodWeek2 = await GetRequestIdAsync(goodPersonId, FeedbackRequestStage.NewStarterWeek2);
        await DispatchAsync(goodWeek2);
        await SubmitViaRequestAsync(goodWeek2, goodPocId);

        var poorWeek2 = await GetRequestIdAsync(poorPersonId, FeedbackRequestStage.NewStarterWeek2);
        await DispatchAsync(poorWeek2);
        _time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));

        await using var context = CreateContext();
        var result = await CreateHistoryService(context).GetProjectPocPatternsAsync(
            _projectId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(ProjectPocPatternsStatus.Success, result.Status);
        var byId = result.Entries!.ToDictionary(e => e.PocId);
        Assert.Equal(0, byId[goodPocId].ConsecutiveNoResponseCount);
        Assert.Equal(1, byId[poorPocId].ConsecutiveNoResponseCount);
    }

    [Fact]
    public async Task ALineManager_OnlySeesPocsUnderTheirOwnReportsOnThatProject()
    {
        Guid lineManagerId;
        await using (var setupContext = CreateContext())
        {
            var manager = new Person { FullName = "Morgan Manager", PracticeId = _practiceId };
            setupContext.People.Add(manager);
            await setupContext.SaveChangesAsync();
            lineManagerId = manager.Id;
        }

        var (ownReportPersonId, ownReportPocId) = await CreatePersonWithPocAsync(lineManagerId);
        var (otherPersonId, otherPocId) = await CreatePersonWithPocAsync(lineManagerId: null);

        await using var context = CreateContext();
        var result = await CreateHistoryService(context).GetProjectPocPatternsAsync(
            _projectId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var visibleIds = result.Entries!.Select(e => e.PocId).ToHashSet();
        Assert.Contains(ownReportPocId, visibleIds);
        Assert.DoesNotContain(otherPocId, visibleIds);
    }

    [Fact]
    public async Task AnUnknownProjectId_ReturnsProjectNotFound()
    {
        await using var context = CreateContext();
        var result = await CreateHistoryService(context).GetProjectPocPatternsAsync(
            Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(ProjectPocPatternsStatus.ProjectNotFound, result.Status);
    }
}
