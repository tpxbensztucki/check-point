using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-241 directly against CatchUpService.RecordOutcomeAsync.
public class CatchUpServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Guid _practiceId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        context.Practices.Add(practice);
        await context.SaveChangesAsync();

        _practiceId = practice.Id;
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

    private async Task<Guid> CreateLineManagerAsync()
    {
        await using var context = CreateContext();
        var manager = new Person { FullName = "Morgan Manager", PracticeId = _practiceId };
        context.People.Add(manager);
        await context.SaveChangesAsync();
        return manager.Id;
    }

    private async Task<(Guid PersonId, Guid CatchUpId)> CreatePersonWithAPendingCatchUpAsync(Guid lineManagerId)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Reviewee", PracticeId = _practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var result = await CreateCycleService(context).TriggerAdHocReviewAsync(
            person.Id, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        return (person.Id, result.Review!.CatchUp.Id);
    }

    [Fact]
    public async Task RecordingNoActionClosed_ResolvesTheCatchUpAndClearsUnderReview()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (personId, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.NoActionClosed, notes: null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RecordOutcomeStatus.Recorded, result.Status);
        Assert.Equal(CatchUpStatus.Recorded, result.CatchUp!.Status);
        Assert.NotNull(result.CatchUp.RecordedAt);

        var person = await context.People.SingleAsync(p => p.Id == personId);
        Assert.Null(person.UnderReviewSince);
    }

    [Fact]
    public async Task RecordingEscalateFurther_LeavesUnderReviewInPlace()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (personId, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.EscalateFurther, notes: "Needs Practice Lead input",
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RecordOutcomeStatus.Recorded, result.Status);

        var person = await context.People.SingleAsync(p => p.Id == personId);
        Assert.NotNull(person.UnderReviewSince);
    }

    [Fact]
    public async Task APracticeLeadOfThePersonsPractice_CanRecordAnOutcome()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (_, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        var lead = new Person { FullName = "Lee Lead", PracticeId = _practiceId };
        context.People.Add(lead);
        await context.SaveChangesAsync();
        var practice = await context.Practices.SingleAsync(p => p.Id == _practiceId);
        practice.PracticeLeadId = lead.Id;
        await context.SaveChangesAsync();

        var result = await CreateCatchUpService(context).RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.SixWeekCheckInAdded, notes: null,
            lead.Id, callerIsAdmin: false, callerIsPracticeLead: true, callerIsLineManager: false);

        Assert.Equal(RecordOutcomeStatus.Recorded, result.Status);
    }

    [Fact]
    public async Task AnUnrelatedCaller_CannotRecordAnOutcome()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (_, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.NoActionClosed, notes: null,
            Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RecordOutcomeStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task AnAlreadyRecordedCatchUp_CannotBeRecordedAgain()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (_, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        var service = CreateCatchUpService(context);
        await service.RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.NoActionClosed, notes: null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var second = await service.RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.NoActionClosed, notes: null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RecordOutcomeStatus.AlreadyRecorded, second.Status);
    }

    [Fact]
    public async Task OtherWithoutNotes_IsRejectedAsInvalid()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (_, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.Other, notes: "  ",
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RecordOutcomeStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task OtherWithNotes_IsAccepted()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (_, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.Other, notes: "Something else happened",
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RecordOutcomeStatus.Recorded, result.Status);
        Assert.Equal("Something else happened", result.CatchUp!.OutcomeNotes);
    }

    [Fact]
    public async Task AnUnknownCatchUpId_ReturnsCatchUpNotFound()
    {
        await using var context = CreateContext();
        var result = await CreateCatchUpService(context).RecordOutcomeAsync(
            Guid.NewGuid(), CatchUpOutcomeType.NoActionClosed, notes: null,
            Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RecordOutcomeStatus.CatchUpNotFound, result.Status);
    }

    [Fact]
    public async Task AfterResolution_ANewFlagOrAdHocTriggerCreatesAFreshCatchUp()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var (personId, catchUpId) = await CreatePersonWithAPendingCatchUpAsync(lineManagerId);

        await using var context = CreateContext();
        await CreateCatchUpService(context).RecordOutcomeAsync(
            catchUpId, CatchUpOutcomeType.NoActionClosed, notes: null,
            lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        var second = await CreateCycleService(context).TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.False(second.Review!.AlreadyPending);
        Assert.NotEqual(catchUpId, second.Review.CatchUp.Id);
        Assert.Equal(2, await context.CatchUps.CountAsync(c => c.PersonId == personId));
    }
}
