using System.Net;
using System.Net.Http.Json;
using CheckPoint.Api.Auth;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

public class PersonRoleEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _nonAdminPersonId;
    private Guid _practiceId;

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
        _practiceId = practice.Id;

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        var nonAdmin = new Person { FullName = "Sam NonAdmin", PracticeId = practice.Id };
        db.People.AddRange(admin, nonAdmin);
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _nonAdminPersonId = nonAdmin.Id;
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
    public async Task Admin_CanAssignLineManagerRole()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.LineManager, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PersonRolesResponse>();
        Assert.Contains(RoleNames.LineManager, body!.Roles);
    }

    [Fact]
    public async Task PersonCanHoldMultipleRolesSimultaneously()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.LineManager, null));

        var response = await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.PracticeLead, _practiceId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PersonRolesResponse>();
        Assert.Contains(RoleNames.LineManager, body!.Roles);
        Assert.Contains(RoleNames.PracticeLead, body.Roles);
    }

    [Fact]
    public async Task AssigningPracticeLead_SetsThePracticesLead()
    {
        using var client = CreateClient(_adminPersonId);

        await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.PracticeLead, _practiceId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var practice = await db.Practices.SingleAsync(p => p.Id == _practiceId);
        Assert.Equal(_nonAdminPersonId, practice.PracticeLeadId);
    }

    [Fact]
    public async Task AssigningPracticeLead_WithoutAPracticeId_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.PracticeLead, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssigningARoleThePersonAlreadyHolds_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/people/{_adminPersonId}/roles", new AssignRoleRequest(RoleNames.Admin, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssigningAnInvalidRoleName_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest("Not A Role", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanRemoveARoleWithoutAffectingOtherRoles()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.LineManager, null));
        await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.PracticeLead, _practiceId));

        var response = await client.DeleteAsync($"/people/{_nonAdminPersonId}/roles/{RoleNames.LineManager}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PersonRolesResponse>();
        Assert.DoesNotContain(RoleNames.LineManager, body!.Roles);
        Assert.Contains(RoleNames.PracticeLead, body.Roles);
    }

    [Fact]
    public async Task RemovingPracticeLeadRole_ClearsThePracticesLead()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.PracticeLead, _practiceId));

        await client.DeleteAsync($"/people/{_nonAdminPersonId}/roles/{RoleNames.PracticeLead}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var practice = await db.Practices.SingleAsync(p => p.Id == _practiceId);
        Assert.Null(practice.PracticeLeadId);
    }

    [Fact]
    public async Task RemovingARoleThePersonDoesNotHold_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.DeleteAsync($"/people/{_nonAdminPersonId}/roles/{RoleNames.LineManager}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemovingAPersonsLastRole_LeavesThemUnableToAuthenticate()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.LineManager, null));
        await client.DeleteAsync($"/people/{_nonAdminPersonId}/roles/{RoleNames.LineManager}");

        using var asFormerLineManager = CreateClient(_nonAdminPersonId);
        var response = await asFormerLineManager.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.LineManager, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotAssignRoles()
    {
        using var client = CreateClient(_nonAdminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.LineManager, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotAssignRoles()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/people/{_nonAdminPersonId}/roles", new AssignRoleRequest(RoleNames.LineManager, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
