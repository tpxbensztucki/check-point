using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-233 directly against FeedbackSubmissionService, the same style
// as MagicLinkServiceTests and FeedbackCycleServiceTests, since it needs a real
// FeedbackRequest -> ProjectMembership -> Person chain rather than the bare Guid
// used by the magic-link-only tests.
public class FeedbackSubmissionServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
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

    private static SubmitFeedbackRequest ValidRequest() => new(
        DoingWell: "Great communication.",
        NotDoingWell: "Sometimes misses deadlines.",
        NeedsToImprove: "Follow up on action items sooner.");

    private async Task<(Guid RequestId, Guid PocId, string Token)> ScheduleRequestAndIssueLinkAsync(Guid? lineManagerId = null)
    {
        await using var context = CreateContext();
        var practiceId = await context.Practices.Select(p => p.Id).SingleAsync();
        var person = new Person { FullName = "Riley Newstarter", PracticeId = practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var projectService = new ProjectService(context, _time, Options.Create(new NewStarterCycleOptions()));
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

        var feedbackRequest = await context.FeedbackRequests
            .SingleAsync(r => r.ProjectMembershipId == membership.Id && r.Stage == FeedbackRequestStage.NewStarterWeek2);

        var link = await new MagicLinkService(context, _time).IssueAsync(feedbackRequest.Id, poc.Id);
        return (feedbackRequest.Id, poc.Id, link.Token);
    }

    private async Task<Guid> CreateLineManagerAsync()
    {
        await using var context = CreateContext();
        var practiceId = await context.Practices.Select(p => p.Id).SingleAsync();
        var manager = new Person { FullName = "Morgan Manager", PracticeId = practiceId };
        context.People.Add(manager);
        await context.SaveChangesAsync();
        return manager.Id;
    }

    [Fact]
    public async Task ValidSubmission_SavesContentAgainstTheRequestAndPoc()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (requestId, pocId, token) = await ScheduleRequestAndIssueLinkAsync(lineManagerId);

        await using var context = CreateContext();
        var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        var result = await service.SubmitAsync(token, ValidRequest());

        Assert.Equal(FeedbackSubmissionStatus.Submitted, result.Status);

        await using var verify = CreateContext();
        var submission = await verify.FeedbackSubmissions.SingleAsync(s => s.FeedbackRequestId == requestId);
        Assert.Equal("Great communication.", submission.DoingWell);
        Assert.Equal(pocId, submission.PocId);
    }

    [Fact]
    public async Task DifferentPocsCanEachSubmitIndependentlyForTheSameRequest()
    {
        var (requestId, firstPocId, firstToken) = await ScheduleRequestAndIssueLinkAsync();

        await using (var context = CreateContext())
        {
            var secondPoc = new Poc
            {
                ProjectMembershipId = (await context.FeedbackRequests.SingleAsync(r => r.Id == requestId)).ProjectMembershipId,
                Name = "Casey Client",
                Email = "casey@client.example.com",
                Relationship = PocRelationship.Client,
                Role = PocRole.Dm,
            };
            context.Pocs.Add(secondPoc);
            await context.SaveChangesAsync();
            var secondLink = await new MagicLinkService(context, _time).IssueAsync(requestId, secondPoc.Id);

            var firstService = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
            var firstResult = await firstService.SubmitAsync(firstToken, ValidRequest());
            var secondResult = await firstService.SubmitAsync(secondLink.Token, ValidRequest());

            Assert.Equal(FeedbackSubmissionStatus.Submitted, firstResult.Status);
            Assert.Equal(FeedbackSubmissionStatus.Submitted, secondResult.Status);
        }

        await using var verify = CreateContext();
        var submissions = await verify.FeedbackSubmissions.Where(s => s.FeedbackRequestId == requestId).ToListAsync();
        Assert.Equal(2, submissions.Count);
        Assert.Contains(submissions, s => s.PocId == firstPocId);
    }

    [Fact]
    public async Task ValidSubmission_QueuesADurableLmNotification()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (requestId, _, token) = await ScheduleRequestAndIssueLinkAsync(lineManagerId);

        await using var context = CreateContext();
        var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        await service.SubmitAsync(token, ValidRequest());

        await using var verify = CreateContext();
        var submission = await verify.FeedbackSubmissions.SingleAsync(s => s.FeedbackRequestId == requestId);
        var notification = await verify.LmNotifications.SingleAsync(n => n.FeedbackSubmissionId == submission.Id);
        Assert.Equal(lineManagerId, notification.LineManagerId);
        Assert.Null(notification.SentAt);
    }

    [Fact]
    public async Task ValidSubmission_WithNoLineManagerAssigned_StillSucceedsWithoutQueuingANotification()
    {
        var (requestId, _, token) = await ScheduleRequestAndIssueLinkAsync(lineManagerId: null);

        await using var context = CreateContext();
        var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        var result = await service.SubmitAsync(token, ValidRequest());

        Assert.Equal(FeedbackSubmissionStatus.Submitted, result.Status);

        await using var verify = CreateContext();
        var submission = await verify.FeedbackSubmissions.SingleAsync(s => s.FeedbackRequestId == requestId);
        Assert.False(await verify.LmNotifications.AnyAsync(n => n.FeedbackSubmissionId == submission.Id));
    }

    [Fact]
    public async Task SubmittingTwiceWithTheSameLink_TheSecondAttemptIsRejectedAsAlreadyUsed()
    {
        var (_, _, token) = await ScheduleRequestAndIssueLinkAsync();

        await using (var context = CreateContext())
        {
            var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
            await service.SubmitAsync(token, ValidRequest());
        }

        await using var secondContext = CreateContext();
        var secondService = new FeedbackSubmissionService(secondContext, _time, new MagicLinkService(secondContext, _time));
        var secondResult = await secondService.SubmitAsync(token, ValidRequest());

        Assert.Equal(FeedbackSubmissionStatus.LinkAlreadyUsed, secondResult.Status);
        Assert.Equal(1, await secondContext.FeedbackSubmissions.CountAsync());
    }

    [Fact]
    public async Task AnEmptyRequiredField_IsRejectedAsInvalid_WithoutConsumingTheLink()
    {
        var (_, _, token) = await ScheduleRequestAndIssueLinkAsync();
        var invalidRequest = ValidRequest() with { NeedsToImprove = "  " };

        await using var context = CreateContext();
        var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        var result = await service.SubmitAsync(token, invalidRequest);

        Assert.Equal(FeedbackSubmissionStatus.Invalid, result.Status);
        Assert.Contains("NeedsToImprove", result.Errors!.Keys);

        var validateResult = await new MagicLinkService(context, _time).ValidateAsync(token);
        Assert.Equal(MagicLinkValidationStatus.Valid, validateResult.Status);
    }

    [Fact]
    public async Task AFieldOverTwoThousandCharacters_IsRejectedAsInvalid()
    {
        var (_, _, token) = await ScheduleRequestAndIssueLinkAsync();
        var invalidRequest = ValidRequest() with { DoingWell = new string('a', FeedbackSubmissionService.MaxFieldLength + 1) };

        await using var context = CreateContext();
        var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        var result = await service.SubmitAsync(token, invalidRequest);

        Assert.Equal(FeedbackSubmissionStatus.Invalid, result.Status);
        Assert.Contains("DoingWell", result.Errors!.Keys);
    }

    [Fact]
    public async Task AnExpiredLink_IsRejectedAsExpired()
    {
        var (_, _, token) = await ScheduleRequestAndIssueLinkAsync();
        _time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));

        await using var context = CreateContext();
        var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));
        var result = await service.SubmitAsync(token, ValidRequest());

        Assert.Equal(FeedbackSubmissionStatus.LinkExpired, result.Status);
    }

    [Fact]
    public async Task AnUnknownToken_IsRejectedAsNotFound()
    {
        await using var context = CreateContext();
        var service = new FeedbackSubmissionService(context, _time, new MagicLinkService(context, _time));

        var result = await service.SubmitAsync("does-not-exist", ValidRequest());

        Assert.Equal(FeedbackSubmissionStatus.LinkNotFound, result.Status);
    }
}
