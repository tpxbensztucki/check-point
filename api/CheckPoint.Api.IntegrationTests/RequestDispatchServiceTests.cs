using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.IntegrationTests;

// Exercises CBLT-234 directly against RequestDispatchService, the same style as
// FeedbackCycleServiceTests, since a due FeedbackRequest needs a real
// ProjectMembership/Poc/Person chain rather than a bare Guid.
public class RequestDispatchServiceTests : IntegrationTestBase
{
    private Guid _projectId;
    private Guid _practiceId;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await using var context = CreateContext();
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        context.Practices.Add(practice);
        var project = new Project { Name = "Website Revamp" };
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        _practiceId = practice.Id;
        _projectId = project.Id;
    }

    private RequestDispatchService CreateService(CheckPointDbContext context, RecordingEmailSender emailSender) =>
        CreateDispatchService(context, emailSender);

    // Schedules a New Starter cycle (via ProjectService, same as
    // FeedbackCycleServiceTests) and returns the id of its 2-week request, whose
    // ScheduledFor is exactly JoinedAt + 2 weeks given the default settings.
    private async Task<(Guid PersonId, Guid RequestId)> ScheduleRequestAsync()
    {
        await using var context = CreateContext();
        var person = new Person { FullName = "Riley Newstarter", PracticeId = _practiceId };
        context.People.Add(person);
        await context.SaveChangesAsync();

        var projectService = new ProjectService(context, Time, CreateAdminSettingsService(context));
        await projectService.AddPersonAsync(_projectId, person.Id);

        var request = await context.FeedbackRequests.SingleAsync(
            r => r.ProjectMembership.ProjectId == _projectId && r.ProjectMembership.PersonId == person.Id
                && r.Stage == FeedbackRequestStage.NewStarterWeek2);
        return (person.Id, request.Id);
    }

    private async Task<Guid> AddPocAsync(Guid personId, string name, string email, PocRelationship relationship)
    {
        await using var context = CreateContext();
        var membership = await context.ProjectMemberships.SingleAsync(m => m.ProjectId == _projectId && m.PersonId == personId);
        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = name,
            Email = email,
            Relationship = relationship,
            Role = PocRole.Tech,
        };
        context.Pocs.Add(poc);
        await context.SaveChangesAsync();
        return poc.Id;
    }

    [Fact]
    public async Task ADueRequest_EmailsEveryCurrentlyAssignedPocWithADistinctLink_AndMarksItSent()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);
        await AddPocAsync(personId, "Casey External", "casey@client.example.com", PocRelationship.External);
        Time.Advance(TimeSpan.FromDays(14));

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Equal(2, emailSender.Sent.Count);
        Assert.Contains(emailSender.Sent, e => e.To == "jamie@example.com");
        Assert.Contains(emailSender.Sent, e => e.To == "casey@client.example.com");

        var tokens = emailSender.Sent
            .Select(e => e.Body.Split("/feedback/")[1].Split('\n')[0].Trim())
            .ToList();
        Assert.Equal(2, tokens.Distinct().Count());

        await using var verify = CreateContext();
        foreach (var token in tokens)
        {
            var result = await new MagicLinkService(verify, Time).ValidateAsync(token);
            Assert.Equal(MagicLinkValidationStatus.Valid, result.Status);
            Assert.Equal(requestId, result.FeedbackRequestId);
        }

        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Sent, request.Status);
    }

    [Fact]
    public async Task ARequestNotYetDue_IsNotDispatched()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Scheduled, request.Status);
    }

    [Fact]
    public async Task WhenModeIsManual_ADueRequestIsNotDispatchedAutomatically()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);
        Time.Advance(TimeSpan.FromDays(14));

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            // ScheduleRequestAsync's own ProjectService call already lazily
            // created the singleton settings row — update it in place rather
            // than adding a second one.
            var settings = await context.AppSettings.SingleAsync();
            settings.AutomaticRequestSendingEnabled = false;
            await context.SaveChangesAsync();

            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Scheduled, request.Status);
    }

    [Fact]
    public async Task SwitchingToManualAfterwards_DoesNotRetroactivelyAffectAnAlreadySentRequest()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);
        Time.Advance(TimeSpan.FromDays(14));

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Single(emailSender.Sent);

        // Switching to Manual after the send only governs future automatic
        // passes — it never touches a request that's already Sent.
        await using (var context = CreateContext())
        {
            var settings = await context.AppSettings.SingleAsync();
            settings.AutomaticRequestSendingEnabled = false;
            await context.SaveChangesAsync();

            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Single(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Sent, request.Status);
    }

    [Fact]
    public async Task ADueRequestWithNoPocsAssigned_IsLeftScheduledForRetry()
    {
        var (_, requestId) = await ScheduleRequestAsync();
        Time.Advance(TimeSpan.FromDays(14));

        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Scheduled, request.Status);
    }

    [Fact]
    public async Task ADueRequestForARemovedMembership_IsCancelledInsteadOfSent()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        await using (var context = CreateContext())
        {
            var projectService = new ProjectService(context, Time, CreateAdminSettingsService(context));
            await projectService.RemovePersonAsync(_projectId, personId);
        }

        Time.Advance(TimeSpan.FromDays(14));
        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        Assert.Empty(emailSender.Sent);
        await using var verify = CreateContext();
        var request = await verify.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public async Task Admin_CanManuallyDispatchARequestBeforeItIsDue()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.Dispatched, result.Status);
        Assert.Single(emailSender.Sent);

        var request = await context.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Sent, request.Status);
    }

    [Fact]
    public async Task ACallerWithNoRelationToThePerson_CannotManuallyDispatch()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RequestDispatchStatus.Forbidden, result.Status);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task ThePersonsOwnLineManager_CanManuallyDispatch()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        Guid lineManagerId;
        await using (var context = CreateContext())
        {
            var manager = new Person { FullName = "Morgan Manager", PracticeId = _practiceId };
            context.People.Add(manager);
            await context.SaveChangesAsync();
            lineManagerId = manager.Id;

            var person = await context.People.SingleAsync(p => p.Id == personId);
            person.LineManagerId = lineManagerId;
            await context.SaveChangesAsync();
        }

        var emailSender = new RecordingEmailSender();
        await using var verifyContext = CreateContext();
        var result = await CreateService(verifyContext, emailSender).DispatchManuallyAsync(
            requestId, lineManagerId, callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(RequestDispatchStatus.Dispatched, result.Status);
    }

    [Fact]
    public async Task AnAlreadySentRequest_CannotBeManuallyDispatchedAgain()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var secondResult = await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.NotCurrentlyScheduled, secondResult.Status);
        Assert.Single(emailSender.Sent);
    }

    [Fact]
    public async Task ManuallyDispatchingWithNoPocsAssigned_ReturnsNoPocsAssigned()
    {
        var (_, requestId) = await ScheduleRequestAsync();

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.NoPocsAssigned, result.Status);
    }

    [Fact]
    public async Task AnUnknownRequestId_ReturnsRequestNotFound()
    {
        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).DispatchManuallyAsync(
            Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(RequestDispatchStatus.RequestNotFound, result.Status);
    }

    private static string ExtractToken(SentEmail email) =>
        email.Body.Split("/feedback/")[1].Split('\n')[0].Trim();

    // CBLT-316 regression fakes: unlike RecordingEmailSender, these actually
    // throw, so tests can assert that a send failure never leaves link/status
    // state half-applied — the whole point of the fix.
    private class AlwaysThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated send failure");
    }

    private class ThrowsForOneAddressEmailSender(string throwingAddress) : IEmailSender
    {
        public List<SentEmail> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (to == throwingAddress)
            {
                throw new InvalidOperationException("Simulated send failure");
            }

            Sent.Add(new SentEmail(to, subject, body));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WhenTheReminderEmailFailsToSend_ThePriorLinkIsStillInvalidatedAndTheNewLinkStillExists()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        var originalToken = ExtractToken(emailSender.Sent.Single());

        var throwingService = CreateDispatchService(context, new AlwaysThrowingEmailSender());
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwingService.SendReminderAsync(
            requestId, pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false));

        var magicLinkService = new MagicLinkService(context, Time);
        var originalResult = await magicLinkService.ValidateAsync(originalToken);
        Assert.Equal(MagicLinkValidationStatus.Superseded, originalResult.Status);

        var stillValidLinks = await context.MagicLinks
            .Where(l => l.FeedbackRequestId == requestId && l.PocId == pocId && l.InvalidatedAt == null)
            .ToListAsync();
        var newLink = Assert.Single(stillValidLinks);
        var newResult = await magicLinkService.ValidateAsync(newLink.Token);
        Assert.Equal(MagicLinkValidationStatus.Valid, newResult.Status);
    }

    [Fact]
    public async Task WhenOnePocsSendFailsDuringDispatch_EveryPocsLinkAndTheRequestsSentStatusAreStillPersisted()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocAId = await AddPocAsync(personId, "Ali PocA", "ali@example.com", PocRelationship.Internal);
        var pocBId = await AddPocAsync(personId, "Blair PocB", "blair@example.com", PocRelationship.Internal);

        await using var context = CreateContext();
        var emailSender = new ThrowsForOneAddressEmailSender("blair@example.com");
        var service = CreateDispatchService(context, emailSender);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false));

        var request = await context.FeedbackRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(FeedbackRequestStatus.Sent, request.Status);

        var links = await context.MagicLinks.Where(l => l.FeedbackRequestId == requestId).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.PocId == pocAId);
        Assert.Contains(links, l => l.PocId == pocBId);
    }

    [Fact]
    public async Task AReminder_IssuesAFreshLinkAndInvalidatesThePriorOne()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        var originalToken = ExtractToken(emailSender.Sent.Single());

        var result = await service.SendReminderAsync(
            requestId, pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(ReminderStatus.Sent, result.Status);
        Assert.Equal(2, emailSender.Sent.Count);
        var newToken = ExtractToken(emailSender.Sent[1]);
        Assert.NotEqual(originalToken, newToken);

        var magicLinkService = new MagicLinkService(context, Time);
        var originalResult = await magicLinkService.ValidateAsync(originalToken);
        Assert.Equal(MagicLinkValidationStatus.Superseded, originalResult.Status);

        var newResult = await magicLinkService.ValidateAsync(newToken);
        Assert.Equal(MagicLinkValidationStatus.Valid, newResult.Status);
    }

    [Fact]
    public async Task AReminder_CanBeTriggeredMultipleTimesWithoutDuplicatingTheRequest()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        await service.SendReminderAsync(
            requestId, pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        var secondResult = await service.SendReminderAsync(
            requestId, pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(ReminderStatus.Sent, secondResult.Status);
        Assert.Equal(3, emailSender.Sent.Count);

        var requestCount = await context.FeedbackRequests.CountAsync(r => r.Id == requestId);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task AReminderForAPocWhoAlreadySubmitted_IsUnavailable()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var dispatchService = CreateService(context, emailSender);
        await dispatchService.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        var token = ExtractToken(emailSender.Sent.Single());

        var submissionService = new FeedbackSubmissionService(context, Time, new MagicLinkService(context, Time));
        await submissionService.SubmitAsync(token, new CheckPoint.Api.Contracts.SubmitFeedbackRequest(
            "Great work.", "Nothing much.", "Keep it up."));

        var result = await dispatchService.SendReminderAsync(
            requestId, pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(ReminderStatus.AlreadySubmitted, result.Status);
        Assert.Single(emailSender.Sent);
    }

    [Fact]
    public async Task AReminderForARequestNotYetDispatched_IsRejected()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).SendReminderAsync(
            requestId, pocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(ReminderStatus.NotYetDispatched, result.Status);
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotSendAReminder()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var result = await service.SendReminderAsync(
            requestId, pocId, Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(ReminderStatus.Forbidden, result.Status);
        Assert.Single(emailSender.Sent);
    }

    [Fact]
    public async Task AnUnknownPocId_ReturnsPocNotFound()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var result = await service.SendReminderAsync(
            requestId, Guid.NewGuid(), Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(ReminderStatus.PocNotFound, result.Status);
    }

    [Fact]
    public async Task BeforeDispatch_EveryPocIsNotYetSent()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).GetPocStatusesAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PocStatusViewStatus.Success, result.Status);
        Assert.Equal(PocResponseStatus.NotYetSent, result.Entries!.Single().Status);
    }

    [Fact]
    public async Task AfterDispatchButBeforeExpiry_ThePocIsSent()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var result = await service.GetPocStatusesAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PocResponseStatus.Sent, result.Entries!.Single().Status);
    }

    [Fact]
    public async Task AfterSevenDaysWithNoSubmission_ThePocIsNoResponse()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var service = CreateService(context, emailSender);
        await service.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));

        var result = await service.GetPocStatusesAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PocResponseStatus.NoResponse, result.Entries!.Single().Status);
    }

    [Fact]
    public async Task APocWhoSubmitted_IsSubmittedEvenAfterExpiry()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var pocId = await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var dispatchService = CreateService(context, emailSender);
        await dispatchService.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        var token = ExtractToken(emailSender.Sent.Single());

        var submissionService = new FeedbackSubmissionService(context, Time, new MagicLinkService(context, Time));
        await submissionService.SubmitAsync(token, new CheckPoint.Api.Contracts.SubmitFeedbackRequest(
            "Great work.", "Nothing much.", "Keep it up."));

        Time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));

        var result = await dispatchService.GetPocStatusesAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PocResponseStatus.Submitted, result.Entries!.Single(e => e.PocId == pocId).Status);
    }

    [Fact]
    public async Task ARequestWithThreePocs_ShowsAMixOfOutcomesNotASingleStatus()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        var submittedPocId = await AddPocAsync(personId, "Alex Submitted", "alex@example.com", PocRelationship.Internal);
        var noResponsePocId = await AddPocAsync(personId, "Blair NoResponse", "blair@example.com", PocRelationship.Internal);
        var pendingPocId = await AddPocAsync(personId, "Casey Pending", "casey@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var dispatchService = CreateService(context, emailSender);
        await dispatchService.DispatchManuallyAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var submittedEmail = emailSender.Sent.Single(e => e.To == "alex@example.com");
        var submissionService = new FeedbackSubmissionService(context, Time, new MagicLinkService(context, Time));
        await submissionService.SubmitAsync(ExtractToken(submittedEmail), new CheckPoint.Api.Contracts.SubmitFeedbackRequest(
            "Great work.", "Nothing much.", "Keep it up."));

        // Advance past expiry for everyone; the submitted POC should stay
        // Submitted regardless, and the pending one gets a reminder (fresh
        // link, fresh expiry) so it stays Sent instead of falling to NoResponse.
        Time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));
        await dispatchService.SendReminderAsync(
            requestId, pendingPocId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var result = await dispatchService.GetPocStatusesAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        var byId = result.Entries!.ToDictionary(e => e.PocId);
        Assert.Equal(PocResponseStatus.Submitted, byId[submittedPocId].Status);
        Assert.Equal(PocResponseStatus.NoResponse, byId[noResponsePocId].Status);
        Assert.Equal(PocResponseStatus.Sent, byId[pendingPocId].Status);
    }

    [Fact]
    public async Task ACancelledRequest_ShowsCancelledForEveryPoc()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        await using (var context = CreateContext())
        {
            var projectService = new ProjectService(context, Time, CreateAdminSettingsService(context));
            await projectService.RemovePersonAsync(_projectId, personId);
        }

        Time.Advance(TimeSpan.FromDays(14));
        var emailSender = new RecordingEmailSender();
        await using (var context = CreateContext())
        {
            await CreateService(context, emailSender).DispatchDueAutomaticRequestsAsync();
        }

        await using var verify = CreateContext();
        var result = await CreateService(verify, emailSender).GetPocStatusesAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        Assert.Equal(PocResponseStatus.Cancelled, result.Entries!.Single().Status);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotViewPocStatuses()
    {
        var (personId, requestId) = await ScheduleRequestAsync();
        await AddPocAsync(personId, "Jamie Internal", "jamie@example.com", PocRelationship.Internal);

        var emailSender = new RecordingEmailSender();
        await using var context = CreateContext();
        var result = await CreateService(context, emailSender).GetPocStatusesAsync(
            requestId, Guid.NewGuid(), callerIsAdmin: false, callerIsPracticeLead: false, callerIsLineManager: true);

        Assert.Equal(PocStatusViewStatus.Forbidden, result.Status);
    }
}
