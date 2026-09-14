using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-239 directly against FeedbackCycleService.FlagCheckInAsync,
// the caller-aware wrapper around the pre-existing HandleCheckInFlaggedAsync
// hook (already covered by CatchUpHookTests.cs) — this file only tests the
// authorization/wiring layer added on top, not the hook's own effects.
public class FlagCheckInServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Guid _practiceId;
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

    private static FeedbackCycleService CreateService(CheckPointDbContext context) =>
        new(context, TimeProvider.System, new AdminSettingsService(context));

    private async Task<(Guid PersonId, Guid RequestId)> CreatePersonWithAGeneralRequestAsync(Guid? lineManagerId = null)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Reviewee", PracticeId = _practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = _projectId, PersonId = person.Id, JoinedAt = DateTimeOffset.UtcNow };
        context.ProjectMemberships.Add(membership);
        await context.SaveChangesAsync();

        var request = new FeedbackRequest
        {
            ProjectMembershipId = membership.Id,
            ScheduledFor = DateTimeOffset.UtcNow,
            Stage = FeedbackRequestStage.General,
        };
        context.FeedbackRequests.Add(request);
        await context.SaveChangesAsync();

        return (person.Id, request.Id);
    }

    private async Task<Guid> CreateLineManagerAsync()
    {
        await using var context = CreateContext();
        var manager = new Person { FullName = "Morgan Manager", PracticeId = _practiceId };
        context.People.Add(manager);
        await context.SaveChangesAsync();
        return manager.Id;
    }

    [Fact]
    public async Task Admin_CanFlagAnyCheckIn()
    {
        var (_, requestId) = await CreatePersonWithAGeneralRequestAsync();

        await using var context = CreateContext();
        var result = await CreateService(context).FlagCheckInAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsLineManager: false);

        Assert.Equal(FlagStatus.Flagged, result.Status);
        Assert.Equal(CatchUpStatus.Pending, result.CatchUp!.Status);
    }

    [Fact]
    public async Task ThePersonsOwnLineManager_CanFlagTheirCheckIn()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (_, requestId) = await CreatePersonWithAGeneralRequestAsync(lineManagerId);

        await using var context = CreateContext();
        var result = await CreateService(context).FlagCheckInAsync(
            requestId, lineManagerId, callerIsAdmin: false, callerIsLineManager: true);

        Assert.Equal(FlagStatus.Flagged, result.Status);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotFlagTheirCheckIn()
    {
        var (_, requestId) = await CreatePersonWithAGeneralRequestAsync();

        await using var context = CreateContext();
        var result = await CreateService(context).FlagCheckInAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: false, callerIsLineManager: true);

        Assert.Equal(FlagStatus.Forbidden, result.Status);
        Assert.False(await context.CatchUps.AnyAsync(c => c.FeedbackRequestId == requestId));
    }

    [Fact]
    public async Task AnUnknownRequestId_ReturnsRequestNotFound()
    {
        await using var context = CreateContext();
        var result = await CreateService(context).FlagCheckInAsync(
            Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsLineManager: false);

        Assert.Equal(FlagStatus.RequestNotFound, result.Status);
    }

    [Fact]
    public async Task FlaggingTheFourWeekCheckIn_AlsoSchedulesTheSixWeekOne()
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Newstarter", PracticeId = _practiceId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var projectService = new ProjectService(context, time, new AdminSettingsService(context));
        await projectService.AddPersonAsync(_projectId, person.Id);

        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == person.Id);
        var fourWeekRequest = await context.FeedbackRequests.SingleAsync(
            r => r.ProjectMembershipId == membership.Id && r.Stage == FeedbackRequestStage.NewStarterWeek4);

        var result = await CreateService(context).FlagCheckInAsync(
            fourWeekRequest.Id, Guid.NewGuid(), callerIsAdmin: true, callerIsLineManager: false);

        Assert.Equal(FlagStatus.Flagged, result.Status);
        Assert.True(await context.FeedbackRequests.AnyAsync(
            r => r.ProjectMembershipId == membership.Id && r.Stage == FeedbackRequestStage.NewStarterWeek6));
    }
}
