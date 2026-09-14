using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-243 directly against DashboardService.GetOutstandingRequestsAsync.
public class DashboardServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
    private Guid _projectId;
    private Guid _practiceId;
    private Guid _otherPracticeId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var otherPractice = new Practice { Name = "Design", Department = new Department { Name = "Tech & Data" } };
        context.Practices.AddRange(practice, otherPractice);
        var project = new Project { Name = "Website Revamp" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        _practiceId = practice.Id;
        _otherPracticeId = otherPractice.Id;
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

    private DashboardService CreateService(CheckPointDbContext context) => new(context, _time);

    private RequestDispatchService CreateDispatchService(CheckPointDbContext context, RecordingEmailSender emailSender) =>
        new(
            context,
            _time,
            emailSender,
            new MagicLinkService(context, _time),
            new AdminSettingsService(context),
            Options.Create(new FrontendOptions()));

    private async Task<Guid> CreatePersonAsync(Guid practiceId)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Newstarter", PracticeId = practiceId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var projectService = new ProjectService(context, _time, new AdminSettingsService(context));
        await projectService.AddPersonAsync(_projectId, person.Id);
        return person.Id;
    }

    private async Task<Guid> GetTwoWeekRequestIdAsync(Guid personId)
    {
        await using var context = CreateContext();
        var request = await context.FeedbackRequests.SingleAsync(
            r => r.ProjectMembership.ProjectId == _projectId && r.ProjectMembership.PersonId == personId
                && r.Stage == FeedbackRequestStage.NewStarterWeek2);
        return request.Id;
    }

    private async Task DispatchTwoWeekRequestAsync(Guid personId)
    {
        var requestId = await GetTwoWeekRequestIdAsync(personId);
        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        await CreateDispatchService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
    }

    private async Task<Guid> AddPocAsync(Guid personId, string name, string email)
    {
        await using var context = CreateContext();
        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == personId);
        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = name,
            Email = email,
            Relationship = PocRelationship.Internal,
            Role = PocRole.Tech,
        };
        context.Pocs.Add(poc);
        await context.SaveChangesAsync();
        return poc.Id;
    }

    [Fact]
    public async Task Admin_SeesOutstandingRequestsAcrossPractices()
    {
        var personA = await CreatePersonAsync(_practiceId);
        await AddPocAsync(personA, "Jamie A", "jamie.a@example.com");
        await DispatchTwoWeekRequestAsync(personA);
        var personB = await CreatePersonAsync(_otherPracticeId);
        await AddPocAsync(personB, "Jamie B", "jamie.b@example.com");
        await DispatchTwoWeekRequestAsync(personB);

        await using var context = CreateContext();
        var result = await CreateService(context).GetOutstandingRequestsAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(2, result.Select(e => e.PersonId).Distinct().Count());
        Assert.All(result, e => Assert.Equal(FeedbackRequestStage.NewStarterWeek2, e.Stage));
    }

    [Fact]
    public async Task APracticeLead_OnlySeesTheirOwnPracticesOutstandingRequests()
    {
        var personA = await CreatePersonAsync(_practiceId);
        await AddPocAsync(personA, "Jamie A", "jamie.a@example.com");
        await DispatchTwoWeekRequestAsync(personA);
        var personB = await CreatePersonAsync(_otherPracticeId);
        await AddPocAsync(personB, "Jamie B", "jamie.b@example.com");
        await DispatchTwoWeekRequestAsync(personB);

        Guid leadId;
        await using (var context = CreateContext())
        {
            var lead = new Person { FullName = "Lee Lead", PracticeId = _practiceId };
            context.People.Add(lead);
            await context.SaveChangesAsync();
            leadId = lead.Id;

            var practice = await context.Practices.SingleAsync(p => p.Id == _practiceId);
            practice.PracticeLeadId = leadId;
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var result = await CreateService(verify).GetOutstandingRequestsAsync(
            leadId, callerIsAdmin: false, callerIsPracticeLead: true, callerIsLineManager: false);

        Assert.Single(result);
        Assert.Equal(personA, result[0].PersonId);
    }

    [Fact]
    public async Task ACancelledRequest_IsExcluded()
    {
        var personId = await CreatePersonAsync(_practiceId);
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com");

        await using (var context = CreateContext())
        {
            var projectService = new ProjectService(context, _time, new AdminSettingsService(context));
            await projectService.RemovePersonAsync(_projectId, personId);
        }

        _time.Advance(TimeSpan.FromDays(14));
        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateDispatchService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        await using var verify = CreateContext();
        var result = await CreateService(verify).GetOutstandingRequestsAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ARequestFullySubmittedByEveryPoc_IsExcluded()
    {
        var personId = await CreatePersonAsync(_practiceId);
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com");
        var requestId = await GetTwoWeekRequestIdAsync(personId);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var dispatchService = CreateDispatchService(context, emailSender);
        await dispatchService.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var token = emailSender.Sent.Single().Body.Split("/feedback/")[1].Split('\n')[0].Trim();
        var submissionService = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        await submissionService.SubmitAsync(token, new SubmitFeedbackRequest("Great work.", "Nothing much.", "Keep it up."));

        var result = await CreateService(context).GetOutstandingRequestsAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.DoesNotContain(result, e => e.FeedbackRequestId == requestId && e.PocId == pocId);
    }

    [Fact]
    public async Task ARequestSubmittedByOnePocButNotAnother_ShowsOnlyTheUnresolvedPoc()
    {
        var personId = await CreatePersonAsync(_practiceId);
        var submittedPocId = await AddPocAsync(personId, "Alex Submitted", "alex@example.com");
        var pendingPocId = await AddPocAsync(personId, "Blair Pending", "blair@example.com");
        var requestId = await GetTwoWeekRequestIdAsync(personId);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var dispatchService = CreateDispatchService(context, emailSender);
        await dispatchService.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var submittedEmail = emailSender.Sent.Single(e => e.To == "alex@example.com");
        var token = submittedEmail.Body.Split("/feedback/")[1].Split('\n')[0].Trim();
        var submissionService = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        await submissionService.SubmitAsync(token, new SubmitFeedbackRequest("Great work.", "Nothing much.", "Keep it up."));

        var result = await CreateService(context).GetOutstandingRequestsAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var entriesForRequest = result.Where(e => e.FeedbackRequestId == requestId).ToList();
        Assert.Single(entriesForRequest);
        Assert.Equal(pendingPocId, entriesForRequest[0].PocId);
        Assert.Equal(PocResponseStatus.Sent, entriesForRequest[0].Status);
    }
}
