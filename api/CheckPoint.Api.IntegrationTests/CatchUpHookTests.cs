using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-230 (the unified flag action's always-on effects: Under Review
// status and a pending catch-up record) directly against
// FeedbackCycleService.HandleCheckInFlaggedAsync — there's no HTTP endpoint yet,
// since the flag action itself doesn't exist (CBLT-239, Milestone 8).
public class CatchUpHookTests : IAsyncLifetime
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

    private static FeedbackCycleService CreateService(CheckPointDbContext context) =>
        new(context, TimeProvider.System, new AdminSettingsService(context));

    private async Task<Guid> ScheduleNewStarterCycleAsync()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await using var context = CreateContext();
        var projectService = new ProjectService(context, time, new AdminSettingsService(context));
        await projectService.AddPersonAsync(_projectId, _personId);

        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == _personId);
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
    public async Task FlaggingAGeneralCycleCheckIn_SetsUnderReviewAndCreatesACatchUp_WithNoSixWeekInsert()
    {
        await using var setup = CreateContext();
        var practice = await setup.Practices.SingleAsync();
        var project = new Project { Name = "Ongoing Project" };
        setup.Projects.Add(project);
        var membership = new ProjectMembership
        {
            ProjectId = project.Id, PersonId = _personId, JoinedAt = DateTimeOffset.UtcNow,
        };
        setup.ProjectMemberships.Add(membership);
        await setup.SaveChangesAsync();

        var generalRequest = new FeedbackRequest
        {
            ProjectMembershipId = membership.Id,
            ScheduledFor = DateTimeOffset.UtcNow,
            Stage = FeedbackRequestStage.General,
        };
        setup.FeedbackRequests.Add(generalRequest);
        await setup.SaveChangesAsync();

        await using (var context = CreateContext())
        {
            await CreateService(context).HandleCheckInFlaggedAsync(generalRequest.Id);
        }

        await using var verify = CreateContext();
        var person = await verify.People.SingleAsync(p => p.Id == _personId);
        Assert.NotNull(person.UnderReviewSince);

        var catchUp = await verify.CatchUps.SingleAsync(c => c.FeedbackRequestId == generalRequest.Id);
        Assert.Equal(_personId, catchUp.PersonId);
        Assert.Equal(CatchUpStatus.Pending, catchUp.Status);

        var sixWeekRequests = await verify.FeedbackRequests.Where(r => r.Stage == FeedbackRequestStage.NewStarterWeek6).ToListAsync();
        Assert.Empty(sixWeekRequests);
    }

    [Fact]
    public async Task FlaggingTheFourWeekCheckIn_SetsUnderReview_CreatesACatchUp_AndInsertsTheSixWeekRequest()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var fourWeekRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek4);

        await using (var context = CreateContext())
        {
            await CreateService(context).HandleCheckInFlaggedAsync(fourWeekRequestId);
        }

        await using var verify = CreateContext();
        var person = await verify.People.SingleAsync(p => p.Id == _personId);
        Assert.NotNull(person.UnderReviewSince);

        Assert.True(await verify.CatchUps.AnyAsync(c => c.FeedbackRequestId == fourWeekRequestId));
        Assert.True(await verify.FeedbackRequests.AnyAsync(
            r => r.ProjectMembershipId == membershipId && r.Stage == FeedbackRequestStage.NewStarterWeek6));
    }

    [Fact]
    public async Task FlaggingTheSameCheckInTwice_DoesNotCreateADuplicateCatchUp()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var fourWeekRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek4);

        await using (var context = CreateContext())
        {
            var service = CreateService(context);
            await service.HandleCheckInFlaggedAsync(fourWeekRequestId);
            await service.HandleCheckInFlaggedAsync(fourWeekRequestId);
        }

        await using var verify = CreateContext();
        var catchUpCount = await verify.CatchUps.CountAsync(c => c.FeedbackRequestId == fourWeekRequestId);
        Assert.Equal(1, catchUpCount);

        var sixWeekCount = await verify.FeedbackRequests.CountAsync(
            r => r.ProjectMembershipId == membershipId && r.Stage == FeedbackRequestStage.NewStarterWeek6);
        Assert.Equal(1, sixWeekCount);
    }

    [Fact]
    public async Task FlaggingDifferentCheckInsForTheSamePerson_CreatesASeparateCatchUpEach()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();
        var twoWeekRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek2);
        var fourWeekRequestId = await GetRequestIdAsync(membershipId, FeedbackRequestStage.NewStarterWeek4);

        await using (var context = CreateContext())
        {
            var service = CreateService(context);
            await service.HandleCheckInFlaggedAsync(twoWeekRequestId);
            await service.HandleCheckInFlaggedAsync(fourWeekRequestId);
        }

        await using var verify = CreateContext();
        var catchUpCount = await verify.CatchUps.CountAsync(c => c.PersonId == _personId);
        Assert.Equal(2, catchUpCount);
    }
}
