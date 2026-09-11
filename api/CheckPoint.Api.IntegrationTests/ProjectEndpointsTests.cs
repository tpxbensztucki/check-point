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

public class ProjectEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _nonAdminPersonId;
    private Guid _employedPersonId;
    private Guid _leaverPersonId;

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
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        var nonAdmin = new Person { FullName = "Sam NonAdmin", PracticeId = practice.Id };
        var employed = new Person { FullName = "Ely Employed", PracticeId = practice.Id };
        var leaver = new Person { FullName = "Lou Leaver", PracticeId = practice.Id, Status = PersonStatus.Leaver };
        db.People.AddRange(admin, nonAdmin, employed, leaver);
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _nonAdminPersonId = nonAdmin.Id;
        _employedPersonId = employed.Id;
        _leaverPersonId = leaver.Id;
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
    public async Task Admin_CanCreateProject_DefaultsToActive()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.Equal("Website Revamp", project!.Name);
        Assert.Equal(ProjectStatus.Active, project.Status);
    }

    [Fact]
    public async Task Admin_CanAddAnEmployedPersonToAProject()
    {
        using var client = CreateClient(_adminPersonId);
        var projectResponse = await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp"));
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectResponse>();

        var response = await client.PostAsJsonAsync(
            $"/projects/{project!.Id}/people", new AddPersonToProjectRequest(_employedPersonId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var membership = await response.Content.ReadFromJsonAsync<ProjectMembershipResponse>();
        Assert.Equal(project.Id, membership!.ProjectId);
        Assert.Equal(_employedPersonId, membership.PersonId);
    }

    [Fact]
    public async Task PersonCanBeAddedToMultipleConcurrentProjects()
    {
        using var client = CreateClient(_adminPersonId);
        var projectA = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Project A")))
            .Content.ReadFromJsonAsync<ProjectResponse>();
        var projectB = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Project B")))
            .Content.ReadFromJsonAsync<ProjectResponse>();

        var responseA = await client.PostAsJsonAsync(
            $"/projects/{projectA!.Id}/people", new AddPersonToProjectRequest(_employedPersonId));
        var responseB = await client.PostAsJsonAsync(
            $"/projects/{projectB!.Id}/people", new AddPersonToProjectRequest(_employedPersonId));

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }

    [Fact]
    public async Task AddingALeaverToAProject_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);
        var project = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp")))
            .Content.ReadFromJsonAsync<ProjectResponse>();

        var response = await client.PostAsJsonAsync(
            $"/projects/{project!.Id}/people", new AddPersonToProjectRequest(_leaverPersonId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddingTheSamePersonToAProjectTwice_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);
        var project = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp")))
            .Content.ReadFromJsonAsync<ProjectResponse>();
        await client.PostAsJsonAsync($"/projects/{project!.Id}/people", new AddPersonToProjectRequest(_employedPersonId));

        var response = await client.PostAsJsonAsync(
            $"/projects/{project.Id}/people", new AddPersonToProjectRequest(_employedPersonId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddingAPersonToANonexistentProject_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/projects/{Guid.NewGuid()}/people", new AddPersonToProjectRequest(_employedPersonId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanRemoveAPersonFromAProject_WithoutDeletingHistory()
    {
        using var client = CreateClient(_adminPersonId);
        var project = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp")))
            .Content.ReadFromJsonAsync<ProjectResponse>();
        await client.PostAsJsonAsync($"/projects/{project!.Id}/people", new AddPersonToProjectRequest(_employedPersonId));

        var response = await client.DeleteAsync($"/projects/{project.Id}/people/{_employedPersonId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var membership = await db.ProjectMemberships.SingleAsync(
            m => m.ProjectId == project.Id && m.PersonId == _employedPersonId);
        Assert.NotNull(membership.RemovedAt);
    }

    [Fact]
    public async Task RemovingAPersonWithNoActiveMembership_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);
        var project = await (await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp")))
            .Content.ReadFromJsonAsync<ProjectResponse>();

        var response = await client.DeleteAsync($"/projects/{project!.Id}/people/{_employedPersonId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotCreateProject()
    {
        using var client = CreateClient(_nonAdminPersonId);

        var response = await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotCreateProject()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/projects", new CreateProjectRequest("Website Revamp"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
