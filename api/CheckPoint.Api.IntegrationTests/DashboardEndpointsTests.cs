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

// Thin HTTP-level smoke test for CBLT-243's endpoint — the scoping and
// exclusion rules themselves are exercised directly against DashboardService
// in DashboardServiceTests.cs.
public class DashboardEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;

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
        _ = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
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

    private CheckPointDbContext CreateDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<CheckPointDbContext>();

    [Fact]
    public async Task AnAuthenticatedAdmin_GetsAnOkResponse()
    {
        await using var db = CreateDb();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        db.People.Add(admin);
        await db.SaveChangesAsync();

        using var client = CreateClient(admin.Id);
        var response = await client.GetAsync("/dashboard/outstanding-requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<OutstandingRequestEntry>>();
        Assert.Empty(entries!);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewOutstandingRequests()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/dashboard/outstanding-requests");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAuthenticatedAdmin_GetsAnOkResponseForFlaggedPeople()
    {
        await using var db = CreateDb();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        db.People.Add(admin);
        await db.SaveChangesAsync();

        using var client = CreateClient(admin.Id);
        var response = await client.GetAsync("/dashboard/flagged-people");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<FlaggedPersonEntry>>();
        Assert.Empty(entries!);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewFlaggedPeople()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/dashboard/flagged-people");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
