using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-234 directly against RequestDispatchService, the same style as
// FeedbackCycleServiceTests, since a due FeedbackRequest needs a real
// ProjectMembership/Poc/Person chain rather than a bare Guid.
public class RequestDispatchServiceTests : IAsyncLifetime
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

    private RequestDispatchService CreateService(
        CheckPointDbContext context, RecordingEmailSender emailSender, RequestDispatchMode mode = RequestDispatchMode.Automatic) =>
        new(
            context,
            _time,
            emailSender,
            new MagicLinkService(context, _time),
            Options.Create(new RequestDispatchOptions { Mode = mode }),
            Options.Create(new FrontendOptions()));

    // Schedules a New Starter cycle (via ProjectService, same as
    // FeedbackCycleServiceTests) and returns the id of its 2-week request, whose
    // ScheduledFor is exactly JoinedAt + 2 weeks given the default
    // NewStarterCycleOptions.
    private async Task<(Guid PersonId, Guid RequestId)> ScheduleRequestAsync()
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Newstarter", PracticeId = _practiceId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var projectService = new ProjectService(context, _time, Options.Create(new NewStarterCycleOptions()));
        await projectService.AddPersonAsync(_projectId, person.Id);

        var request = await context.FeedbackRequests.SingleAsync(
            r => r.ProjectMembership.ProjectId == _projectId && r.ProjectMembership.PersonId == person.Id
                && r.Stage == FeedbackRequestStage.NewStarterWeek2);
        return (person.Id, request.Id);
    }

    private async Task<Guid> AddPocAsync(Guid personId, string name, string email, PocRelationship relationship)
    {
        await using var context = CreateContext();
        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == personId);
        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = name,
            Email = email,
            Relationship = relationship,
            Role = PocRole.Tech,
        };
        context.Pocs.Add(poc);
        await context.SaveChangesAsync();
        return poc.Id;
    }

    [Fact]
    public async Task ADueRequest_EmailsEveryCurrentlyAssignedPocWithADistinctLink_AndMarksItSent()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);
        await AddPocAsync(personId, "Casey External", "casey@client.example.com", PocRelationship.External);
        _time.Advance(TimeSpan.FromDays(14));

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Equal(2, emailSender.Sent.Count);
        Assert.Contains(emailSender.Sent, e => e.To == "jamie@example.com");
        Assert.Contains(emailSender.Sent, e => e.To == "casey@client.example.com");

        var tokens = emailSender.Sent
            .Select(e => e.Body.Split("/feedback/")[1].Split('\n')[0].Trim())
            .ToList();
        Assert.Equal(2, tokens.Distinct().Count());

        await using var verify = CreateContext();
        foreach (var token in tokens)
        {
            var result = await new MagicLinkService(verify, _time).ValidateAsync(token);
            Assert.Equal(MagicLinkValidationStatus.Valid, result.Status);
            Assert.Equal(requestId, result.FeedbackRequestId);
        }

        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Sent, request.Status);
    }

    [Fact]
    public async Task ARequestNotYetDue_IsNotDispatched()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Scheduled, request.Status);
    }

    [Fact]
    public async Task WhenModeIsManual_ADueRequestIsNotDispatchedAutomatically()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);
        _time.Advance(TimeSpan.FromDays(14));

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender, RequestDispatchMode.Manual).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Scheduled, request.Status);
    }

    [Fact]
    public async Task ADueRequestWithNoPocsAssigned_IsLeftScheduledForRetry()
    {
        var (_, requestId) = await ScheduleRequestAsync();
        _time.Advance(TimeSpan.FromDays(14));

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Scheduled, request.Status);
    }

    [Fact]
    public async Task ADueRequestForARemovedMembership_IsCancelledInsteadOfSent()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        await using (var context = CreateContext())
        {
            var projectService = new ProjectService(context, _time, Options.Create(new NewStarterCycleOptions()));
            await projectService.RemovePersonAsync(_projectId, personId);
        }

        _time.Advance(TimeSpan.FromDays(14));
        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public async Task Admin_CanManuallyDispatchARequestBeforeItIsDue()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.Dispatched, result.Status);
        Assert.Single(emailSender.Sent);

        var request = await context.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Sent, request.Status);
    }

    [Fact]
    public async Task ACallerWithNoRelationToThePerson_CannotManuallyDispatch()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RequestDispatchStatus.Forbidden, result.Status);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task ThePersonsOwnLineManager_CanManuallyDispatch()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        Guid lineManagerId;
        await using (var context = CreateContext())
        {
            var manager = new Person { FullName = "Morgan Manager", PracticeId = _practiceId };
            context.People.Add(manager);
            await context.SaveChangesAsync();
            lineManagerId = manager.Id;

            var person = await context.People.SingleAsync(p => p.Id == personId);
            person.LineManagerId = lineManagerId;
            await context.SaveChangesAsync();
        }

        var emailSender = new RecordingEmailSender();
        await using var verifyContext = CreateContext();
        var result = await CreateService(verifyContext, emailSender).DispatchManuallyAsync(
            requestId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RequestDispatchStatus.Dispatched, result.Status);
    }

    [Fact]
    public async Task AnAlreadySentRequest_CannotBeManuallyDispatchedAgain()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var secondResult = await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.NotCurrentlyScheduled, secondResult.Status);
        Assert.Single(emailSender.Sent);
    }

    [Fact]
    public async Task ManuallyDispatchingWithNoPocsAssigned_ReturnsNoPocsAssigned()
    {
        var (_, requestId) = await ScheduleRequestAsync();

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.NoPocsAssigned, result.Status);
    }

    [Fact]
    public async Task AnUnknownRequestId_ReturnsRequestNotFound()
    {
        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.RequestNotFound, result.Status);
    }
}
