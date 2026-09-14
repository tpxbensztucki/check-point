using System.Net;
using CheckPoint.Api.Auth;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

public class FeedbackRequestEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private readonly RecordingEmailSender _emailSender = new();
    private Guid _adminPersonId;
    private Guid _lineManagerPersonId;
    private Guid _otherLineManagerPersonId;
    private Guid _targetPersonId;
    private Guid _requestId;

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
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IEmailSender>(_emailSender);
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();

        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        var lineManager = new Person { FullName = "Morgan Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var otherLineManager = new Person { FullName = "Casey OtherManager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        db.People.AddRange(admin, lineManager, otherLineManager);
        await db.SaveChangesAsync();

        var target = new Person { FullName = "Riley Target", PracticeId = practice.Id, LineManagerId = lineManager.Id };
        var project = new Project { Name = "Website Revamp" };
        db.People.Add(target);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = project.Id, PersonId = target.Id, JoinedAt = DateTimeOffset.UtcNow };
        db.ProjectMemberships.Add(membership);
        await db.SaveChangesAsync();

        db.Pocs.Add(new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = "Jamie POC",
            Email = "jamie@example.com",
            Relationship = PocRelationship.Internal,
            Role = PocRole.Tech,
        });
        var request = new FeedbackRequest
        {
            ProjectMembershipId = membership.Id,
            ScheduledFor = DateTimeOffset.UtcNow.AddDays(7),
            Stage = FeedbackRequestStage.General,
        };
        db.FeedbackRequests.Add(request);
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _lineManagerPersonId = lineManager.Id;
        _otherLineManagerPersonId = otherLineManager.Id;
        _targetPersonId = target.Id;
        _requestId = request.Id;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private HttpClient CreateClient(Guid actingAsPersonId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevPersonAuthenticationHandler.PersonIdHeader, actingAsPersonId.ToString());
        return client;
    }

    [Fact]
    public async Task Admin_CanManuallyDispatchARequest()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsync($"/feedback-requests/{_requestId}/dispatch", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_emailSender.Sent);
    }

    [Fact]
    public async Task ThePersonsOwnLineManager_CanManuallyDispatch()
    {
        using var client = CreateClient(_lineManagerPersonId);

        var response = await client.PostAsync($"/feedback-requests/{_requestId}/dispatch", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_IsForbidden()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.PostAsync($"/feedback-requests/{_requestId}/dispatch", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_emailSender.Sent);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotDispatch()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/feedback-requests/{_requestId}/dispatch", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownRequestId_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsync($"/feedback-requests/{Guid.NewGuid()}/dispatch", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnAlreadySentRequest_CannotBeDispatchedAgain()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsync($"/feedback-requests/{_requestId}/dispatch", null);

        var response = await client.PostAsync($"/feedback-requests/{_requestId}/dispatch", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
