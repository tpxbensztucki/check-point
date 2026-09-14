using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-227 (auto-inserting a 6-week check-in when the New Starter
// cycle's 4-week request is flagged) directly against FeedbackCycleService, the
// same style as MagicLinkServiceTests and ProjectServiceSchedulingTests, since
// there's no HTTP endpoint yet — the flag action itself doesn't exist until
// CBLT-239/CBLT-230.
public class FeedbackCycleServiceTests : IAsyncLifetime
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

    private async Task<Guid> ScheduleNewStarterCycleAsync()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await using var context = CreateContext();
        var projectService = new ProjectService(context, time, new AdminSettingsService(context));
        await projectService.AddPersonAsync(_projectId, _personId);

        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == _personId);
        return membership.Id;
    }

    [Fact]
    public async Task FlaggingTheFourWeekCheckIn_SchedulesAnAdditionalSixWeekRequest()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();

        await using (var context = CreateContext())
        {
            var fourWeekRequest = await context.FeedbackRequests.SingleAsync(
                r => r.ProjectMembershipId == membershipId && r.Stage == FeedbackRequestStage.NewStarterWeek4);

            var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));
            await service.HandleCheckInFlaggedAsync(fourWeekRequest.Id);
        }

        await using var verify = CreateContext();
        var sixWeekRequests = await verify.FeedbackRequests
            .Where(r => r.ProjectMembershipId == membershipId && r.Stage == FeedbackRequestStage.NewStarterWeek6)
            .ToListAsync();

        Assert.Single(sixWeekRequests);
        Assert.Equal(FeedbackRequestStatus.Scheduled, sixWeekRequests[0].Status);
    }

    [Theory]
    [InlineData(FeedbackRequestStage.NewStarterWeek2)]
    [InlineData(FeedbackRequestStage.NewStarterWeek8)]
    public async Task FlaggingAnyOtherStage_DoesNotInsertASixWeekRequest(FeedbackRequestStage stage)
    {
        var membershipId = await ScheduleNewStarterCycleAsync();

        await using (var context = CreateContext())
        {
            var request = await context.FeedbackRequests.SingleAsync(
                r => r.ProjectMembershipId == membershipId && r.Stage == stage);

            var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));
            await service.HandleCheckInFlaggedAsync(request.Id);
        }

        await using var verify = CreateContext();
        var sixWeekRequests = await verify.FeedbackRequests
            .Where(r => r.ProjectMembershipId == membershipId && r.Stage == FeedbackRequestStage.NewStarterWeek6)
            .ToListAsync();

        Assert.Empty(sixWeekRequests);
    }

    [Fact]
    public async Task FlaggingTheFourWeekCheckInTwice_DoesNotDuplicateTheSixWeekRequest()
    {
        var membershipId = await ScheduleNewStarterCycleAsync();

        await using (var context = CreateContext())
        {
            var fourWeekRequest = await context.FeedbackRequests.SingleAsync(
                r => r.ProjectMembershipId == membershipId && r.Stage == FeedbackRequestStage.NewStarterWeek4);

            var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));
            await service.HandleCheckInFlaggedAsync(fourWeekRequest.Id);
            await service.HandleCheckInFlaggedAsync(fourWeekRequest.Id);
        }

        await using var verify = CreateContext();
        var sixWeekRequests = await verify.FeedbackRequests
            .Where(r => r.ProjectMembershipId == membershipId && r.Stage == FeedbackRequestStage.NewStarterWeek6)
            .ToListAsync();

        Assert.Single(sixWeekRequests);
    }

    [Fact]
    public async Task FlaggingAnUnknownRequestId_IsANoOp()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        var service = new FeedbackCycleService(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));

        await service.HandleCheckInFlaggedAsync(Guid.NewGuid());

        Assert.Empty(await context.FeedbackRequests.ToListAsync());
    }
}
