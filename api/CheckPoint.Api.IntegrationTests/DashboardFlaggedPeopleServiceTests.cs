using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-244 directly against DashboardService.GetFlaggedPeopleAsync.
public class DashboardFlaggedPeopleServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
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
        await context.SaveChangesAsync();

        _practiceId = practice.Id;
        _otherPracticeId = otherPractice.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private CheckPointDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CheckPointDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new CheckPointDbContext(options);
    }

    private static DashboardService CreateDashboardService(CheckPointDbContext context) => new(context, TimeProvider.System);

    private static CatchUpService CreateCatchUpService(CheckPointDbContext context) => new(context, TimeProvider.System);

    private static FeedbackCycleService CreateCycleService(CheckPointDbContext context) =>
        new(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));

    private async Task<Guid> CreateLineManagerAsync(Guid practiceId)
    {
        await using var context = CreateContext();
        var manager = new Person { FullName = "Morgan Manager", PracticeId = practiceId };
        context.People.Add(manager);
        await context.SaveChangesAsync();
        return manager.Id;
    }

    private async Task<Guid> CreatePersonAsync(Guid practiceId, Guid? lineManagerId = null)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Reviewee", PracticeId = practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();
        return person.Id;
    }

    [Fact]
    public async Task Admin_SeesEveryoneCurrentlyUnderReviewAcrossPractices()
    {
        var lineManagerA = await CreateLineManagerAsync(_practiceId);
        var personA = await CreatePersonAsync(_practiceId, lineManagerA);
        var lineManagerB = await CreateLineManagerAsync(_otherPracticeId);
        var personB = await CreatePersonAsync(_otherPracticeId, lineManagerB);

        await using var context = CreateContext();
        var cycleService = CreateCycleService(context);
        await cycleService.TriggerAdHocReviewAsync(
            personA, lineManagerA, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);
        await cycleService.TriggerAdHocReviewAsync(
            personB, lineManagerB, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var result = await CreateDashboardService(context).GetFlaggedPeopleAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.PersonId == personA);
        Assert.Contains(result, e => e.PersonId == personB);
        Assert.All(result, e => Assert.Equal(CatchUpTriggerSource.AdHoc, e.TriggerSource));
    }

    [Fact]
    public async Task APracticeLead_OnlySeesTheirOwnPracticesFlaggedPeople()
    {
        var lineManagerA = await CreateLineManagerAsync(_practiceId);
        var personA = await CreatePersonAsync(_practiceId, lineManagerA);
        var lineManagerB = await CreateLineManagerAsync(_otherPracticeId);
        var personB = await CreatePersonAsync(_otherPracticeId, lineManagerB);

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

            var cycleService = CreateCycleService(context);
            await cycleService.TriggerAdHocReviewAsync(
                personA, lineManagerA, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);
            await cycleService.TriggerAdHocReviewAsync(
                personB, lineManagerB, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);
        }

        await using var verify = CreateContext();
        var result = await CreateDashboardService(verify).GetFlaggedPeopleAsync(
            leadId, callerIsAdmin: false, callerIsPracticeLead: true, callerIsLineManager: false);

        Assert.Single(result);
        Assert.Equal(personA, result[0].PersonId);
    }

    [Fact]
    public async Task ARecordedCatchUp_NeverAppears()
    {
        var lineManagerId = await CreateLineManagerAsync(_practiceId);
        var personId = await CreatePersonAsync(_practiceId, lineManagerId);

        await using var context = CreateContext();
        var cycleService = CreateCycleService(context);
        var triggerResult = await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var catchUpService = CreateCatchUpService(context);
        await catchUpService.RecordOutcomeAsync(
            triggerResult.Review!.CatchUp.Id, CatchUpOutcomeType.NoActionClosed, null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var result = await CreateDashboardService(context).GetFlaggedPeopleAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.DoesNotContain(result, e => e.PersonId == personId);
    }

    // Deliberate reading, not an oversight: EscalateFurther leaves
    // Person.UnderReviewSince set (CBLT-241) but resolves that CatchUp's own
    // Status to Recorded — this view is keyed off "has a Pending CatchUp",
    // so the Person correctly disappears until a fresh flag/trigger opens a
    // new one, even though they're still nominally "under review."
    [Fact]
    public async Task APersonEscalatedFurther_DoesNotAppearUntilAFreshCatchUpExists()
    {
        var lineManagerId = await CreateLineManagerAsync(_practiceId);
        var personId = await CreatePersonAsync(_practiceId, lineManagerId);

        await using var context = CreateContext();
        var cycleService = CreateCycleService(context);
        var triggerResult = await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var catchUpService = CreateCatchUpService(context);
        await catchUpService.RecordOutcomeAsync(
            triggerResult.Review!.CatchUp.Id, CatchUpOutcomeType.EscalateFurther, "Needs more time.",
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var afterEscalation = await CreateDashboardService(context).GetFlaggedPeopleAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        Assert.DoesNotContain(afterEscalation, e => e.PersonId == personId);

        var person = await context.People.SingleAsync(p => p.Id == personId);
        Assert.NotNull(person.UnderReviewSince);

        var secondTrigger = await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);
        Assert.False(secondTrigger.Review!.AlreadyPending);

        var afterFreshTrigger = await CreateDashboardService(context).GetFlaggedPeopleAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        Assert.Contains(afterFreshTrigger, e => e.PersonId == personId);
    }

    [Fact]
    public async Task ACheckInTriggeredEntry_HasCheckInTriggerSource()
    {
        Guid lineManagerId = await CreateLineManagerAsync(_practiceId);
        var personId = await CreatePersonAsync(_practiceId, lineManagerId);

        await using var context = CreateContext();
        var project = new Project { Name = "Website Revamp" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = project.Id, PersonId = personId, JoinedAt = DateTimeOffset.UtcNow };
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

        var cycleService = CreateCycleService(context);
        await cycleService.FlagCheckInAsync(request.Id, lineManagerId, callerIsAdmin: false, callerIsLineManager: true);

        var result = await CreateDashboardService(context).GetFlaggedPeopleAsync(
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var entry = Assert.Single(result, e => e.PersonId == personId);
        Assert.Equal(CatchUpTriggerSource.CheckIn, entry.TriggerSource);
    }
}
