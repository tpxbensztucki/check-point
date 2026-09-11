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

public class DepartmentEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _nonAdminPersonId;

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

        // Force the host (and its startup migration) to build before seeding.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();

        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

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
    public async Task Admin_CanCreateDepartment()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync("/departments", new CreateDepartmentRequest("Tech & Data"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var department = await response.Content.ReadFromJsonAsync<DepartmentResponse>();
        Assert.Equal("Tech & Data", department!.Name);
    }

    [Fact]
    public async Task Admin_CanCreatePracticeUnderAnExistingDepartment()
    {
        using var client = CreateClient(_adminPersonId);
        var departmentResponse = await client.PostAsJsonAsync(
            "/departments", new CreateDepartmentRequest("Tech & Data"));
        var department = await departmentResponse.Content.ReadFromJsonAsync<DepartmentResponse>();

        var response = await client.PostAsJsonAsync(
            $"/departments/{department!.Id}/practices", new CreatePracticeRequest("Software Engineering"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var practice = await response.Content.ReadFromJsonAsync<PracticeResponse>();
        Assert.Equal("Software Engineering", practice!.Name);
        Assert.Equal(department.Id, practice.DepartmentId);
    }

    [Fact]
    public async Task PracticeCannotBeCreatedUnderANonexistentDepartment()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/departments/{Guid.NewGuid()}/practices", new CreatePracticeRequest("Software Engineering"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotCreateDepartment()
    {
        using var client = CreateClient(_nonAdminPersonId);

        var response = await client.PostAsJsonAsync("/departments", new CreateDepartmentRequest("Tech & Data"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotCreateDepartment()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/departments", new CreateDepartmentRequest("Tech & Data"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
