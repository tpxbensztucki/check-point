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

public class PersonCatchUpHistoryEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _lineManagerPersonId;
    private Guid _otherLineManagerPersonId;
    private Guid _reportPersonId;

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
        var lineManager = new Person { FullName = "Lee Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var otherLineManager = new Person { FullName = "Other Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        db.People.AddRange(admin, lineManager, otherLineManager);
        await db.SaveChangesAsync();

        var report = new Person { FullName = "Riley Report", PracticeId = practice.Id, LineManagerId = lineManager.Id };
        db.People.Add(report);
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _lineManagerPersonId = lineManager.Id;
        _otherLineManagerPersonId = otherLineManager.Id;
        _reportPersonId = report.Id;
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
    public async Task Admin_CanViewAnyPersonsHistory()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/people/{_reportPersonId}/catch-ups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<PersonCatchUpHistoryResponse>(JsonTestOptions.Value);
        Assert.Empty(history!.Entries);
    }

    [Fact]
    public async Task ThePersonsOwnLineManager_CanViewTheirHistory()
    {
        using var client = CreateClient(_lineManagerPersonId);

        var response = await client.GetAsync($"/people/{_reportPersonId}/catch-ups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotViewTheirHistory()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.GetAsync($"/people/{_reportPersonId}/catch-ups");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownPerson_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/people/{Guid.NewGuid()}/catch-ups");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewHistory()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/people/{_reportPersonId}/catch-ups");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
