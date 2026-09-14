using System.Net;
using System.Net.Http.Json;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Guest-facing, same as MagicLinkEndpointsTests — no DevPersonId header on any of
// these requests.
public class FeedbackSubmissionEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                });
            });
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(_time));
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> ScheduleRequestAndIssueLinkAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var project = new Project { Name = "Website Revamp" };
        var person = new Person { FullName = "Riley Newstarter", Practice = practice };
        db.Practices.Add(practice);
        db.Projects.Add(project);
        db.People.Add(person);
        await db.SaveChangesAsync();

        var projectService = scope.ServiceProvider.GetRequiredService<ProjectService>();
        await projectService.AddPersonAsync(project.Id, person.Id);

        var membership = await db.ProjectMemberships.SingleAsync(m => m.ProjectId == project.Id && m.PersonId == person.Id);
        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = "Jamie POC",
            Email = "jamie@example.com",
            Relationship = PocRelationship.Internal,
            Role = PocRole.Tech,
        };
        db.Pocs.Add(poc);
        await db.SaveChangesAsync();

        var feedbackRequest = await db.FeedbackRequests.SingleAsync(
            r => r.ProjectMembershipId == membership.Id && r.Stage == FeedbackRequestStage.NewStarterWeek2);

        var magicLinkService = scope.ServiceProvider.GetRequiredService<MagicLinkService>();
        var link = await magicLinkService.IssueAsync(feedbackRequest.Id, poc.Id);
        return link.Token;
    }

    private static readonly SubmitFeedbackRequest ValidBody = new(
        DoingWell: "Great communication.",
        NotDoingWell: "Sometimes misses deadlines.",
        NeedsToImprove: "Follow up on action items sooner.");

    [Fact]
    public async Task ACompleteValidSubmission_IsAcceptedAndConfirmed()
    {
        var token = await ScheduleRequestAndIssueLinkAsync();

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/magic-links/{token}/submission", ValidBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AfterSubmitting_TheSameLinkCanNoLongerBeUsed()
    {
        var token = await ScheduleRequestAndIssueLinkAsync();

        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync($"/magic-links/{token}/submission", ValidBody);

        var viewResponse = await client.GetAsync($"/magic-links/{token}");
        Assert.Equal(HttpStatusCode.Conflict, viewResponse.StatusCode);

        var secondSubmitResponse = await client.PostAsJsonAsync($"/magic-links/{token}/submission", ValidBody);
        Assert.Equal(HttpStatusCode.Conflict, secondSubmitResponse.StatusCode);
    }

    [Fact]
    public async Task AMissingRequiredField_IsRejectedAsABadRequest()
    {
        var token = await ScheduleRequestAndIssueLinkAsync();
        var invalidBody = ValidBody with { NeedsToImprove = "" };

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/magic-links/{token}/submission", invalidBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownToken_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/magic-links/does-not-exist/submission", ValidBody);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
