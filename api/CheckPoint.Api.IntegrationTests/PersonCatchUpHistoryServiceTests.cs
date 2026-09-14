using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-242 directly against CatchUpService.GetHistoryAsync.
public class PersonCatchUpHistoryServiceTests : IAsyncLifetime
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

    private async Task<Guid> CreatePersonAsync(Guid? lineManagerId = null)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Reviewee", PracticeId = _practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();
        return person.Id;
    }

    [Fact]
    public async Task APersonWithTwoResolvedAndOnePendingCatchUp_ShowsAllThreeMostRecentFirst()
    {
        var lineManagerId = await CreateLineManagerAsync(_practiceId);
        var personId = await CreatePersonAsync(lineManagerId);

        await using var context = CreateContext();
        var cycleService = CreateCycleService(context);
        var catchUpService = CreateCatchUpService(context);

        var first = await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);
        await catchUpService.RecordOutcomeAsync(
            first.Review!.CatchUp.Id, CatchUpOutcomeType.NoActionClosed, null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var second = await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);
        await catchUpService.RecordOutcomeAsync(
            second.Review!.CatchUp.Id, CatchUpOutcomeType.SixWeekCheckInAdded, null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var third = await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var result = await catchUpService.GetHistoryAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(PersonCatchUpHistoryStatus.Success, result.Status);
        Assert.Equal(3, result.History!.Entries.Count);
        Assert.Equal(third.Review!.CatchUp.Id, result.History.Entries[0].Id);
        Assert.Equal(CatchUpStatus.Pending, result.History.Entries[0].Status);
        Assert.Equal(CatchUpStatus.Recorded, result.History.Entries[1].Status);
        Assert.Equal(CatchUpStatus.Recorded, result.History.Entries[2].Status);
        Assert.NotNull(result.History.UnderReviewSince);
    }

    [Fact]
    public async Task APersonWithNoHistory_ReturnsAnEmptyListNotAnError()
    {
        var personId = await CreatePersonAsync();

        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).GetHistoryAsync(
            personId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PersonCatchUpHistoryStatus.Success, result.Status);
        Assert.Empty(result.History!.Entries);
        Assert.Null(result.History.UnderReviewSince);
    }

    [Fact]
    public async Task ACheckInTriggeredEntry_HasCheckInTriggerSourceAndAnAdHocOneHasAdHoc()
    {
        var lineManagerId = await CreateLineManagerAsync(_practiceId);
        var personId = await CreatePersonAsync(lineManagerId);

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
        var catchUpService = CreateCatchUpService(context);
        var flagResult = await cycleService.FlagCheckInAsync(request.Id, lineManagerId, callerIsAdmin: false, callerIsLineManager: true);

        // The check-in-triggered catch-up must be resolved first — while it's
        // still Pending, TriggerAdHocReviewAsync's own guard would just
        // surface it again rather than create a second one (CBLT-240).
        await catchUpService.RecordOutcomeAsync(
            flagResult.CatchUp!.Id, CatchUpOutcomeType.NoActionClosed, null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var result = await catchUpService.GetHistoryAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(2, result.History!.Entries.Count);
        var checkInEntry = result.History.Entries.Single(e => e.Id == flagResult.CatchUp!.Id);
        var adHocEntry = result.History.Entries.Single(e => e.Id != flagResult.CatchUp!.Id);
        Assert.Equal(CatchUpTriggerSource.CheckIn, checkInEntry.TriggerSource);
        Assert.Equal(CatchUpTriggerSource.AdHoc, adHocEntry.TriggerSource);
    }

    [Fact]
    public async Task APracticeLeadOfThePersonsPractice_CanViewTheirHistory()
    {
        var lineManagerId = await CreateLineManagerAsync(_practiceId);
        var personId = await CreatePersonAsync(lineManagerId);

        await using var context = CreateContext();
        var lead = new Person { FullName = "Lee Lead", PracticeId = _practiceId };
        context.People.Add(lead);
        await context.SaveChangesAsync();
        var practice = await context.Practices.SingleAsync(p => p.Id == _practiceId);
        practice.PracticeLeadId = lead.Id;
        await context.SaveChangesAsync();

        var result = await CreateCatchUpService(context).GetHistoryAsync(
            personId, lead.Id, callerIsAdmin: false, callerIsPracticeLead: true, callerIsLineManager: false);

        Assert.Equal(PersonCatchUpHistoryStatus.Success, result.Status);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotViewTheirHistory()
    {
        var personId = await CreatePersonAsync();

        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).GetHistoryAsync(
            personId, Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(PersonCatchUpHistoryStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task AnUnknownPersonId_ReturnsPersonNotFound()
    {
        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).GetHistoryAsync(
            Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PersonCatchUpHistoryStatus.PersonNotFound, result.Status);
    }
}
