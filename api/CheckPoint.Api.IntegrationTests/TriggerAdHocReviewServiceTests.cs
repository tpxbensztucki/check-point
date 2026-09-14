using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-240 directly against FeedbackCycleService.TriggerAdHocReviewAsync.
public class TriggerAdHocReviewServiceTests : IAsyncLifetime
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

    private static FeedbackCycleService CreateService(CheckPointDbContext context) =>
        new(context, TimeProvider.System, Options.Create(new GeneralCycleOptions()));

    private async Task<Guid> CreatePersonAsync(Guid? lineManagerId = null, Guid? practiceId = null)
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Reviewee", PracticeId = practiceId ?? _practiceId, LineManagerId = lineManagerId };
        context.People.Add(person);
        await context.SaveChangesAsync();
        return person.Id;
    }

    private async Task<Guid> CreateLineManagerAsync()
    {
        await using var context = CreateContext();
        var manager = new Person { FullName = "Morgan Manager", PracticeId = _practiceId };
        context.People.Add(manager);
        await context.SaveChangesAsync();
        return manager.Id;
    }

    private async Task<Guid> CreatePracticeLeadAsync(Guid practiceId)
    {
        await using var context = CreateContext();
        var lead = new Person { FullName = "Lee Lead", PracticeId = practiceId };
        context.People.Add(lead);
        await context.SaveChangesAsync();

        var practice = await context.Practices.SingleAsync(p => p.Id == practiceId);
        practice.PracticeLeadId = lead.Id;
        await context.SaveChangesAsync();

        return lead.Id;
    }

    [Fact]
    public async Task ThePersonsOwnLineManager_CanTriggerAnAdHocReview()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var personId = await CreatePersonAsync(lineManagerId);

        await using var context = CreateContext();
        var result = await CreateService(context).TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(AdHocReviewStatus.Triggered, result.Status);
        Assert.False(result.Review!.AlreadyPending);
        Assert.Null(result.Review.CatchUp.FeedbackRequestId);

        var person = await context.People.SingleAsync(p => p.Id == personId);
        Assert.NotNull(person.UnderReviewSince);
    }

    [Fact]
    public async Task APracticeLeadOfThePersonsPractice_CanTriggerAnAdHocReview()
    {
        var personId = await CreatePersonAsync();
        var leadId = await CreatePracticeLeadAsync(_practiceId);

        await using var context = CreateContext();
        var result = await CreateService(context).TriggerAdHocReviewAsync(
            personId, leadId, callerIsAdmin: false, callerIsPracticeLead: true, callerIsLineManager: false);

        Assert.Equal(AdHocReviewStatus.Triggered, result.Status);
        Assert.False(result.Review!.AlreadyPending);
    }

    [Fact]
    public async Task APracticeLeadOfADifferentPractice_CannotTriggerAnAdHocReview()
    {
        var personId = await CreatePersonAsync();
        var otherLeadId = await CreatePracticeLeadAsync(_otherPracticeId);

        await using var context = CreateContext();
        var result = await CreateService(context).TriggerAdHocReviewAsync(
            personId, otherLeadId, callerIsAdmin: false, callerIsPracticeLead: true, callerIsLineManager: false);

        Assert.Equal(AdHocReviewStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotTriggerAnAdHocReview()
    {
        var personId = await CreatePersonAsync();

        await using var context = CreateContext();
        var result = await CreateService(context).TriggerAdHocReviewAsync(
            personId, Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(AdHocReviewStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task TriggeringTwice_SurfacesTheExistingPendingCatchUpInsteadOfDuplicating()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var personId = await CreatePersonAsync(lineManagerId);

        await using var context = CreateContext();
        var service = CreateService(context);
        var first = await service.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);
        var second = await service.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.False(first.Review!.AlreadyPending);
        Assert.True(second.Review!.AlreadyPending);
        Assert.Equal(first.Review.CatchUp.Id, second.Review.CatchUp.Id);

        Assert.Equal(1, await context.CatchUps.CountAsync(c => c.PersonId == personId));
    }

    [Fact]
    public async Task AnAdHocReview_NeverInsertsASixWeekRequest()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var personId = await CreatePersonAsync(lineManagerId);

        await using var context = CreateContext();
        await CreateService(context).TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.False(await context.FeedbackRequests.AnyAsync(r => r.Stage == FeedbackRequestStage.NewStarterWeek6));
    }

    [Fact]
    public async Task AnUnknownPersonId_ReturnsPersonNotFound()
    {
        await using var context = CreateContext();
        var result = await CreateService(context).TriggerAdHocReviewAsync(
            Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(AdHocReviewStatus.PersonNotFound, result.Status);
    }

    [Fact]
    public async Task AnAdHocCatchUpAlreadyPending_DoesNotSuppressALaterFourWeekCheckInsSixWeekInsert()
    {
        var lineManagerId = await CreateLineManagerAsync();
        var personId = await CreatePersonAsync(lineManagerId);

        await using var context = CreateContext();
        var cycleService = CreateService(context);
        await cycleService.TriggerAdHocReviewAsync(
            personId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        // Now the same Person has a 4-week New Starter check-in flagged too,
        // on an unrelated Project membership.
        var project = new Project { Name = "Website Revamp" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = project.Id, PersonId = personId, JoinedAt = DateTimeOffset.UtcNow };
        context.ProjectMemberships.Add(membership);
        await context.SaveChangesAsync();

        var fourWeekRequest = new FeedbackRequest
        {
            ProjectMembershipId = membership.Id,
            ScheduledFor = DateTimeOffset.UtcNow,
            Stage = FeedbackRequestStage.NewStarterWeek4,
        };
        context.FeedbackRequests.Add(fourWeekRequest);
        await context.SaveChangesAsync();

        await cycleService.HandleCheckInFlaggedAsync(fourWeekRequest.Id);

        // The pending ad-hoc CatchUp must not have blocked the 6-week insert,
        // even though flagging did not create a *second* CatchUp for this
        // still-pending Person (HandleCheckInFlaggedAsync's own guard is
        // per-FeedbackRequestId, so it still creates its own CatchUp for this
        // specific check-in — that's the existing, unchanged, tested behaviour).
        Assert.True(await context.FeedbackRequests.AnyAsync(
            r => r.ProjectMembershipId == membership.Id && r.Stage == FeedbackRequestStage.NewStarterWeek6));
        Assert.Equal(2, await context.CatchUps.CountAsync(c => c.PersonId == personId));
    }
}
