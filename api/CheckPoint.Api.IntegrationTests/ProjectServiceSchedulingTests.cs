using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises New Starter cycle scheduling (CBLT-226) directly against
// ProjectService, the same style as MagicLinkServiceTests, since it needs
// FakeTimeProvider control over exactly when a Person joins a Project.
public class ProjectServiceSchedulingTests : IAsyncLifetime
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

    [Fact]
    public async Task AddingAPersonToAProject_SchedulesRequestsAtTheDefaultIntervals()
    {
        var joinedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var time = new FakeTimeProvider(joinedAt);

        await using (var context = CreateContext())
        {
            var service = new ProjectService(context, time, new AdminSettingsService(context));
            await service.AddPersonAsync(_projectId, _personId);
        }

        await using var verify = CreateContext();
        var scheduledDates = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.ProjectId == _projectId && r.ProjectMembership.PersonId == _personId)
            .Select(r => r.ScheduledFor)
            .OrderBy(d => d)
            .ToListAsync();

        Assert.Equal(
            [joinedAt.AddDays(14), joinedAt.AddDays(28), joinedAt.AddDays(56)],
            scheduledDates);
    }

    [Fact]
    public async Task AllScheduledRequests_StartInTheScheduledStatus()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        await using (var context = CreateContext())
        {
            var service = new ProjectService(context, time, new AdminSettingsService(context));
            await service.AddPersonAsync(_projectId, _personId);
        }

        await using var verify = CreateContext();
        var statuses = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.ProjectId == _projectId)
            .Select(r => r.Status)
            .ToListAsync();

        Assert.Equal(3, statuses.Count);
        Assert.All(statuses, s => Assert.Equal(FeedbackRequestStatus.Scheduled, s));
    }

    [Fact]
    public async Task ConfiguredIntervals_OverrideTheDefault()
    {
        var joinedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var time = new FakeTimeProvider(joinedAt);

        await using (var context = CreateContext())
        {
            context.AppSettings.Add(new AppSettings { NewStarterIntervalWeeks = [3, 6, 10] });
            await context.SaveChangesAsync();

            var service = new ProjectService(context, time, new AdminSettingsService(context));
            await service.AddPersonAsync(_projectId, _personId);
        }

        await using var verify = CreateContext();
        var scheduledDates = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.ProjectId == _projectId)
            .Select(r => r.ScheduledFor)
            .OrderBy(d => d)
            .ToListAsync();

        Assert.Equal(
            [joinedAt.AddDays(21), joinedAt.AddDays(42), joinedAt.AddDays(70)],
            scheduledDates);
    }

    [Fact]
    public async Task ChangingTheIntervalSetting_DoesNotAffectAnAlreadyScheduledPerson()
    {
        var joinedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var time = new FakeTimeProvider(joinedAt);
        Guid secondPersonId;

        await using (var context = CreateContext())
        {
            var service = new ProjectService(context, time, new AdminSettingsService(context));
            await service.AddPersonAsync(_projectId, _personId);

            var secondPerson = new Person { FullName = "Jordan Newstarter", PracticeId = context.People.Single(p => p.Id == _personId).PracticeId };
            context.People.Add(secondPerson);
            await context.SaveChangesAsync();
            secondPersonId = secondPerson.Id;

            context.AppSettings.Add(new AppSettings { NewStarterIntervalWeeks = [3, 6, 10] });
            await context.SaveChangesAsync();

            await service.AddPersonAsync(_projectId, secondPersonId);
        }

        await using var verify = CreateContext();
        var firstPersonDates = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.PersonId == _personId)
            .Select(r => r.ScheduledFor)
            .OrderBy(d => d)
            .ToListAsync();
        var secondPersonDates = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.PersonId == secondPersonId)
            .Select(r => r.ScheduledFor)
            .OrderBy(d => d)
            .ToListAsync();

        Assert.Equal([joinedAt.AddDays(14), joinedAt.AddDays(28), joinedAt.AddDays(56)], firstPersonDates);
        Assert.Equal([joinedAt.AddDays(21), joinedAt.AddDays(42), joinedAt.AddDays(70)], secondPersonDates);
    }

    [Fact]
    public async Task CompletingAProject_CancelsItsScheduledFeedbackRequests()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        await using (var context = CreateContext())
        {
            var service = new ProjectService(context, time, new AdminSettingsService(context));
            await service.AddPersonAsync(_projectId, _personId);
            await service.CompleteProjectAsync(_projectId);
        }

        await using var verify = CreateContext();
        var statuses = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.ProjectId == _projectId)
            .Select(r => r.Status)
            .ToListAsync();

        Assert.Equal(3, statuses.Count);
        Assert.All(statuses, s => Assert.Equal(FeedbackRequestStatus.Cancelled, s));
    }

    [Fact]
    public async Task CompletingAProject_DoesNotCancelRequestsOnADifferentProject()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        Guid otherProjectId;

        await using (var context = CreateContext())
        {
            var otherProject = new Project { Name = "Second Project" };
            context.Projects.Add(otherProject);
            await context.SaveChangesAsync();
            otherProjectId = otherProject.Id;

            var service = new ProjectService(context, time, new AdminSettingsService(context));
            await service.AddPersonAsync(_projectId, _personId);
            await service.AddPersonAsync(otherProjectId, _personId);
            await service.CompleteProjectAsync(_projectId);
        }

        await using var verify = CreateContext();
        var otherProjectStatuses = await verify.FeedbackRequests
            .Where(r => r.ProjectMembership.ProjectId == otherProjectId)
            .Select(r => r.Status)
            .ToListAsync();

        Assert.Equal(3, otherProjectStatuses.Count);
        Assert.All(otherProjectStatuses, s => Assert.Equal(FeedbackRequestStatus.Scheduled, s));
    }
}
