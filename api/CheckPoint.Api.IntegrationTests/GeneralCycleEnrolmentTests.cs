using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-228 (auto-enrolling into the General cycle when the New Starter
// cycle's final request concludes) directly against FeedbackCycleService — there's
// no HTTP endpoint yet, since neither trigger (guest submission, Milestone 6; No
// Response expiry, CBLT-237) exists.
public class GeneralCycleEnrolmentTests : IAsyncLifetime
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

    private async Task<Guid> ScheduleNewStarterCycleAsync(Guid? projectId = null, Guid? personId = null, FakeTimeProvider? time = null)
    {
        time ??= new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await using var context = CreateContext();
        var projectService = new ProjectService(context, time, new AdminSettingsService(context));
        await projectService.AddPersonAsync(projectId ?? _projectId, personId ?? _personId);

        var membership = await context.ProjectMemberships.SingleAsync(
            m => m.ProjectId == (projectId ?? _projectId) && m.PersonId == (personId ?? _personId));
        return membership.Id;
    }

    private async Task<Guid> GetRequestIdAsync(Guid membershipId, FeedbackRequestStage stage)
    {
        await using var context = CreateContext();
        var request = await context.FeedbackRequests.SingleAsync(
            r => r.ProjectMembershipId == membershipId && r.Stage == stage);
        return request.Id;
    }

    [Fact]
    public async Task FinalNewStarterRequestCompleting_EnrolsIntoTheGeneralCycle()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var finalRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek8);

        await using (var context = CreateContext())
        {
            var service = new FeedbackCycleService(
                context,
                new FakeTimeProvider(DateTimeOffset.Parse("2026-03-01T00:00:00Z")),
                Options.Create(new GeneralCycleOptions()));
            await service.HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using var verify = CreateContext();
        var membership = await verify.ProjectMemberships.SingleAsync(m => m.Id == membershipId);
        Assert.NotNull(membership.GeneralCycleEnrolledAt);
    }

    [Theory]
    [InlineData(FeedbackRequestStage.NewStarterWeek2)]
    [InlineData(FeedbackRequestStage.NewStarterWeek4)]
    public async Task ANonFinalStageCompleting_DoesNotEnrol(FeedbackRequestStage stage)
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var requestId = await GetRequestIdAsync(membershipId, stage);

        await using (var context = CreateContext())
        {
            var service = new FeedbackCycleService(
                context, new FakeTimeProvider(DateTimeOffset.UtcNow), Options.Create(new GeneralCycleOptions()));
            await service.HandleFeedbackRequestCompletedAsync(requestId);
        }

        await using var verify = CreateContext();
        var membership = await verify.ProjectMemberships.SingleAsync(m => m.Id == membershipId);
        Assert.Null(membership.GeneralCycleEnrolledAt);
    }

    [Fact]
    public async Task CompletingTheFinalRequestTwice_DoesNotChangeTheEnrolmentTimestamp()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var finalRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek8);
        var firstCompletionTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-03-01T00:00:00Z"));

        await using (var context = CreateContext())
        {
            var service = new FeedbackCycleService(context, firstCompletionTime, Options.Create(new GeneralCycleOptions()));
            await service.HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using (var context = CreateContext())
        {
            var laterTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-04-01T00:00:00Z"));
            var service = new FeedbackCycleService(context, laterTime, Options.Create(new GeneralCycleOptions()));
            await service.HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using var verify = CreateContext();
        var membership = await verify.ProjectMemberships.SingleAsync(m => m.Id == membershipId);
        Assert.Equal(firstCompletionTime.GetUtcNow(), membership.GeneralCycleEnrolledAt);
    }

    [Fact]
    public async Task AProjectCompletedBeforeTheFinalRequestConcludes_PreventsEnrolment()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var finalRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek8);

        await using (var context = CreateContext())
        {
            var projectService = new ProjectService(
                context, TimeProvider.System, new AdminSettingsService(context));
            await projectService.CompleteProjectAsync(_projectId);
        }

        await using (var context = CreateContext())
        {
            var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));
            await service.HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using var verify = CreateContext();
        var membership = await verify.ProjectMemberships.SingleAsync(m => m.Id == membershipId);
        Assert.Null(membership.GeneralCycleEnrolledAt);
    }

    [Fact]
    public async Task APersonWhoBecameALeaver_IsNotEnrolled()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var finalRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek8);

        await using (var context = CreateContext())
        {
            var person = await context.People.SingleAsync(p => p.Id == _personId);
            person.Status = PersonStatus.Leaver;
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));
            await service.HandleFeedbackRequestCompletedAsync(finalRequestId);
        }

        await using var verify = CreateContext();
        var membership = await verify.ProjectMemberships.SingleAsync(m => m.Id == membershipId);
        Assert.Null(membership.GeneralCycleEnrolledAt);
    }

    [Fact]
    public async Task APersonOnTwoProjects_EnrolsEachIndependently()
    {
        var membershipAId = await ScheduleNewStarterCycleAsync();

        Guid projectBId;
        await using (var context = CreateContext())
        {
            var projectB = new Project { Name = "Second Project" };
            context.Projects.Add(projectB);
            await context.SaveChangesAsync();
            projectBId = projectB.Id;
        }

        var membershipBId = await ScheduleNewStarterCycleAsync(projectId: projectBId);
        var finalRequestAId = await GetRequestIdAsync(membershipAId, FeedbackRequestStage.NewStarterWeek8);

        await using (var context = CreateContext())
        {
            var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));
            await service.HandleFeedbackRequestCompletedAsync(finalRequestAId);
        }

        await using var verify = CreateContext();
        var membershipA = await verify.ProjectMemberships.SingleAsync(m => m.Id == membershipAId);
        var membershipB = await verify.ProjectMemberships.SingleAsync(m => m.Id == membershipBId);
        Assert.NotNull(membershipA.GeneralCycleEnrolledAt);
        Assert.Null(membershipB.GeneralCycleEnrolledAt);
    }

    [Fact]
    public async Task CompletingAnUnknownRequestId_IsANoOp()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));

        await service.HandleFeedbackRequestCompletedAsync(Guid.NewGuid());
    }
}
