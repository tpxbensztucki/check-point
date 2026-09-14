using System.Net;
using System.Net.Http.Json;
using CheckPoint.Api.Auth;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

public class PersonProjectsEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _lineManagerPersonId;
    private Guid _otherLineManagerPersonId;
    private Guid _targetPersonId;

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
        var lineManager = new Person { FullName = "Morgan Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var otherLineManager = new Person { FullName = "Casey OtherManager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        db.People.AddRange(admin, lineManager, otherLineManager);
        await db.SaveChangesAsync();

        var target = new Person { FullName = "Riley Target", PracticeId = practice.Id, LineManagerId = lineManager.Id };
        db.People.Add(target);
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _lineManagerPersonId = lineManager.Id;
        _otherLineManagerPersonId = otherLineManager.Id;
        _targetPersonId = target.Id;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private HttpClient CreateClient(Guid? actingAsPersonId = null)
    {
        var client = _factory.CreateClient();
        if (actingAsPersonId is { } personId)
        {
            client.DefaultRequestHeaders.Add(DevPersonAuthenticationHandler.PersonIdHeader, personId.ToString());
        }

        return client;
    }

    [Fact]
    public async Task Admin_SeesAllOfAPersonsProjects_WithMissingRolesFlaggedOnActiveOnes()
    {
        using var client = CreateClient(_adminPersonId);
        var projectA = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Project A")))
            .Content.ReadFromJsonAsync<ProjectResponse>(JsonTestOptions.Value);
        var projectB = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Project B")))
            .Content.ReadFromJsonAsync<ProjectResponse>(JsonTestOptions.Value);
        await client.PostAsJsonAsync($"/projects/{projectA!.Id}/people", new AddPersonToProjectRequest(_targetPersonId));
        await client.PostAsJsonAsync($"/projects/{projectB!.Id}/people", new AddPersonToProjectRequest(_targetPersonId));
        await client.PostAsJsonAsync(
            $"/projects/{projectA.Id}/people/{_targetPersonId}/pocs",
            new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        var response = await client.GetAsync($"/people/{_targetPersonId}/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<PersonProjectSummary>>(JsonTestOptions.Value);
        Assert.Equal(2, summaries!.Count);
        var summaryA = summaries.Single(s => s.ProjectId == projectA.Id);
        Assert.Contains(PocRole.Dm, summaryA.MissingStandardRoles!);
        Assert.Contains(PocRole.Other, summaryA.MissingStandardRoles!);
        Assert.DoesNotContain(PocRole.Tech, summaryA.MissingStandardRoles!);
    }

    [Fact]
    public async Task CompletedProjects_AreShownWithoutAMissingRolesFlag()
    {
        using var client = CreateClient(_adminPersonId);
        var project = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Project A")))
            .Content.ReadFromJsonAsync<ProjectResponse>(JsonTestOptions.Value);
        await client.PostAsJsonAsync($"/projects/{project!.Id}/people", new AddPersonToProjectRequest(_targetPersonId));
        await client.PostAsync($"/projects/{project.Id}/complete", null);

        var response = await client.GetAsync($"/people/{_targetPersonId}/projects");

        var summaries = await response.Content.ReadFromJsonAsync<List<PersonProjectSummary>>(JsonTestOptions.Value);
        var summary = summaries!.Single(s => s.ProjectId == project.Id);
        Assert.Equal(ProjectStatus.Completed, summary.Status);
        Assert.Null(summary.MissingStandardRoles);
    }

    [Fact]
    public async Task LineManager_CanViewTheirOwnReportsProjects()
    {
        using var admin = CreateClient(_adminPersonId);
        var project = await (await admin.PostAsJsonAsync("/projects", new CreateProjectRequest("Project A")))
            .Content.ReadFromJsonAsync<ProjectResponse>(JsonTestOptions.Value);
        await admin.PostAsJsonAsync($"/projects/{project!.Id}/people", new AddPersonToProjectRequest(_targetPersonId));

        using var client = CreateClient(_lineManagerPersonId);
        var response = await client.GetAsync($"/people/{_targetPersonId}/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LineManager_CannotViewProjectsForSomeoneWhoIsNotTheirReport()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.GetAsync($"/people/{_targetPersonId}/projects");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ViewingProjectsForANonexistentPerson_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/people/{Guid.NewGuid()}/projects");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewAPersonsProjects()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/people/{_targetPersonId}/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
