using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-229 (FY-quarter scheduling with skip-if-due-within-4-weeks logic)
// directly against FeedbackCycleService, the same style as
// GeneralCycleEnrolmentTests/FeedbackCycleServiceTests — there's no HTTP endpoint
// yet, since the triggers that would call this hook don't exist.
public class GeneralCycleSchedulingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Guid _personId;
    private Guid _projectId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        context.Practices.Add(practice);
        await context.SaveChangesAsync();

        var person = new Person { FullName = "Riley Newstarter", PracticeId = practice.Id };
        var project = new Project { Name = "Website Revamp" };
        context.People.Add(person);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        _personId = person.Id;
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

    private async Task<Guid> ScheduleNewStarterCycleAndGetFinalRequestIdAsync()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await using var context = CreateContext();
        var projectService = new ProjectService(context, time, new AdminSettingsService(context));
        await projectService.AddPersonAsync(_projectId, _personId);

        return await context.FeedbackRequests.Where(
            r => r.ProjectMembership.ProjectId == _projectId
                && r.ProjectMembership.PersonId == _personId
                && r.Stage == FeedbackRequestStage.NewStarterWeek8)
            .Select(r => r.Id)
            .SingleAsync();
    }

    private static FeedbackCycleService CreateService(CheckPointDbContext context, DateTimeOffset now) =>
        new(context, new FakeTimeProvider(now), new AdminSettingsService(context));

    [Fact]
    public async Task EnrolmentFiveWeeksBeforeTheNextQuarter_SchedulesThatUpcomingQuarter()
    {
        var finalRequestId = await ScheduleNewStarterCycleAndGetFinalRequestIdAsync();

        // 2026-02-18 is 5 weeks (35 days) before the Apr 1 FY-quarter boundary.
        var enrolmentTime = DateTimeOffset.Parse("2026-02-18T00:00:00Z");
        await using (var context = CreateContext())
        {
            await CreateService(context, enrolmentTime).HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using var verify = CreateContext();
        var generalRequest = await verify.FeedbackRequests.SingleAsync(
            r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General);
        Assert.Equal(DateTimeOffset.Parse("2026-04-01T00:00:00Z"), generalRequest.ScheduledFor);
    }

    [Fact]
    public async Task EnrolmentTwoWeeksBeforeTheNextQuarter_SkipsToTheQuarterAfter()
    {
        var finalRequestId = await ScheduleNewStarterCycleAndGetFinalRequestIdAsync();

        // 2026-03-18 is 2 weeks (14 days) before the Apr 1 FY-quarter boundary.
        var enrolmentTime = DateTimeOffset.Parse("2026-03-18T00:00:00Z");
        await using (var context = CreateContext())
        {
            await CreateService(context, enrolmentTime).HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using var verify = CreateContext();
        var generalRequest = await verify.FeedbackRequests.SingleAsync(
            r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:00Z"), generalRequest.ScheduledFor);
    }

    [Fact]
    public async Task ConfiguringAHigherThreshold_SkipsAQuarterThatTheDefaultWouldNotHave()
    {
        var finalRequestId = await ScheduleNewStarterCycleAndGetFinalRequestIdAsync();

        // 2026-02-18 is 5 weeks (35 days) before the Apr 1 FY-quarter boundary —
        // inside a 6-week threshold, but outside the default 4-week one (see
        // EnrolmentFiveWeeksBeforeTheNextQuarter_SchedulesThatUpcomingQuarter,
        // which asserts the opposite outcome at the default threshold).
        var enrolmentTime = DateTimeOffset.Parse("2026-02-18T00:00:00Z");
        await using (var context = CreateContext())
        {
            // ScheduleNewStarterCycleAndGetFinalRequestIdAsync's own ProjectService
            // call already lazily created the singleton settings row — update it in
            // place rather than adding a second one.
            var settings = await context.AppSettings.SingleAsync();
            settings.GeneralCycleSkipThresholdWeeks = 6;
            await context.SaveChangesAsync();

            await CreateService(context, enrolmentTime).HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using var verify = CreateContext();
        var generalRequest = await verify.FeedbackRequests.SingleAsync(
            r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T00:00:00Z"), generalRequest.ScheduledFor);
    }

    [Fact]
    public async Task SubsequentQuarters_ContinueAutomaticallyOnceInTheGeneralCycle()
    {
        var finalRequestId = await ScheduleNewStarterCycleAndGetFinalRequestIdAsync();

        await using (var context = CreateContext())
        {
            await CreateService(context, DateTimeOffset.Parse("2026-02-18T00:00:00Z"))
                .HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        Guid firstGeneralRequestId;
        await using (var context = CreateContext())
        {
            firstGeneralRequestId = await context.FeedbackRequests.Where(
                r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General)
                .Select(r => r.Id)
                .SingleAsync();
        }

        await using (var context = CreateContext())
        {
            await CreateService(context, DateTimeOffset.Parse("2026-04-05T00:00:00Z"))
                .HandleFeedbackRequestCompletedAsync(firstGeneralRequestId);
        }

        await using var verify = CreateContext();
        var scheduledDates = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General)
            .Select(r => r.ScheduledFor)
            .OrderBy(d => d)
            .ToListAsync();

        Assert.Equal(
            [DateTimeOffset.Parse("2026-04-01T00:00:00Z"), DateTimeOffset.Parse("2026-07-01T00:00:00Z")],
            scheduledDates);
    }

    [Fact]
    public async Task CompletingAGeneralRequestTwice_DoesNotScheduleTwoFollowUps()
    {
        var finalRequestId = await ScheduleNewStarterCycleAndGetFinalRequestIdAsync();

        await using (var context = CreateContext())
        {
            await CreateService(context, DateTimeOffset.Parse("2026-02-18T00:00:00Z"))
                .HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        Guid firstGeneralRequestId;
        await using (var context = CreateContext())
        {
            firstGeneralRequestId = await context.FeedbackRequests.Where(
                r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General)
                .Select(r => r.Id)
                .SingleAsync();
        }

        await using (var context = CreateContext())
        {
            var service = CreateService(context, DateTimeOffset.Parse("2026-04-05T00:00:00Z"));
            await service.HandleFeedbackRequestCompletedAsync(firstGeneralRequestId);
            await service.HandleFeedbackRequestCompletedAsync(firstGeneralRequestId);
        }

        await using var verify = CreateContext();
        var count = await verify.FeedbackRequests.CountAsync(
            r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task AProjectCompletedBeforeTheNextQuarter_StopsFurtherGeneralCycleScheduling()
    {
        var finalRequestId = await ScheduleNewStarterCycleAndGetFinalRequestIdAsync();

        await using (var context = CreateContext())
        {
            await CreateService(context, DateTimeOffset.Parse("2026-02-18T00:00:00Z"))
                .HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        Guid firstGeneralRequestId;
        await using (var context = CreateContext())
        {
            firstGeneralRequestId = await context.FeedbackRequests.Where(
                r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General)
                .Select(r => r.Id)
                .SingleAsync();

            var projectService = new ProjectService(
                context, TimeProvider.System, new AdminSettingsService(context));
            await projectService.CompleteProjectAsync(_projectId);
        }

        await using (var context = CreateContext())
        {
            await CreateService(context, DateTimeOffset.Parse("2026-04-05T00:00:00Z"))
                .HandleFeedbackRequestCompletedAsync(firstGeneralRequestId);
        }

        await using var verify = CreateContext();
        var count = await verify.FeedbackRequests.CountAsync(
            r => r.ProjectMembership.PersonId == _personId && r.Stage == FeedbackRequestStage.General);
        Assert.Equal(1, count);
    }
}
