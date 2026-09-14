using System.Net;
using CheckPoint.Api.Auth;
using CheckPoint.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

public class PocResponseHistoryEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _otherLineManagerPersonId;
    private Guid _projectId;
    private Guid _pocId;

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
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();

        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        var otherLineManager = new Person { FullName = "Casey OtherManager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        db.People.AddRange(admin, otherLineManager);
        await db.SaveChangesAsync();

        var target = new Person { FullName = "Riley Target", PracticeId = practice.Id };
        var project = new Project { Name = "Website Revamp" };
        db.People.Add(target);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = project.Id, PersonId = target.Id, JoinedAt = DateTimeOffset.UtcNow };
        db.ProjectMemberships.Add(membership);
        await db.SaveChangesAsync();

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

        _adminPersonId = admin.Id;
        _otherLineManagerPersonId = otherLineManager.Id;
        _projectId = project.Id;
        _pocId = poc.Id;
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
    public async Task Admin_CanViewAPocsResponseHistory()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/pocs/{_pocId}/response-history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotViewAPocsResponseHistory()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.GetAsync($"/pocs/{_pocId}/response-history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownPoc_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/pocs/{Guid.NewGuid()}/response-history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanViewAProjectsPocResponsePatterns()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/projects/{_projectId}/poc-response-patterns");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ALineManagerWithNoReportsOnTheProject_SeesAnEmptyList()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.GetAsync($"/projects/{_projectId}/poc-response-patterns");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("[]", body);
    }

    [Fact]
    public async Task AnUnknownProject_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/projects/{Guid.NewGuid()}/poc-response-patterns");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
