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

// GET /people/{personId} — the scoped single-Person profile read backing the
// new Person-profile frontend page (CBLT-240 follow-up). Same three-way
// scoping (Admin/Practice Lead/Line Manager) as AdHocReviewEndpointsTests,
// which this file mirrors the setup shape of.
public class PersonProfileEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _lineManagerPersonId;
    private Guid _otherLineManagerPersonId;
    private Guid _practiceLeadPersonId;
    private Guid _otherPracticePersonId;
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
        var practiceLeadRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var otherPractice = new Practice { Name = "Data Science", Department = new Department { Name = "Tech & Data" } };
        db.Practices.AddRange(practice, otherPractice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        var lineManager = new Person { FullName = "Lee Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var otherLineManager = new Person { FullName = "Other Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var practiceLead = new Person { FullName = "Pat Lead", PracticeId = practice.Id, Roles = [practiceLeadRole] };
        db.People.AddRange(admin, lineManager, otherLineManager, practiceLead);
        await db.SaveChangesAsync();

        practice.PracticeLeadId = practiceLead.Id;
        await db.SaveChangesAsync();

        var report = new Person
        {
            FullName = "Riley Report",
            PracticeId = practice.Id,
            LineManagerId = lineManager.Id,
            Email = "riley@example.com",
        };
        var otherPracticePerson = new Person { FullName = "Ollie Outsider", PracticeId = otherPractice.Id };
        db.People.AddRange(report, otherPracticePerson);
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _lineManagerPersonId = lineManager.Id;
        _otherLineManagerPersonId = otherLineManager.Id;
        _practiceLeadPersonId = practiceLead.Id;
        _otherPracticePersonId = otherPracticePerson.Id;
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
    public async Task Admin_CanFetchAnyPerson()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/people/{_reportPersonId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<PersonListEntry>(JsonTestOptions.Value);
        Assert.Equal("Riley Report", entry!.FullName);
        Assert.Equal("Software Engineering", entry.PracticeName);
        Assert.Equal("Lee Manager", entry.LineManagerName);
        Assert.Equal("riley@example.com", entry.Email);
    }

    [Fact]
    public async Task LineManager_CanFetchTheirOwnReport()
    {
        using var client = CreateClient(_lineManagerPersonId);

        var response = await client.GetAsync($"/people/{_reportPersonId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LineManager_CannotFetchAnUnrelatedPerson()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.GetAsync($"/people/{_reportPersonId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PracticeLead_CanFetchSomeoneInTheirPractice()
    {
        using var client = CreateClient(_practiceLeadPersonId);

        var response = await client.GetAsync($"/people/{_reportPersonId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PracticeLead_CannotFetchSomeoneOutsideTheirPractice()
    {
        using var client = CreateClient(_practiceLeadPersonId);

        var response = await client.GetAsync($"/people/{_otherPracticePersonId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FetchingANonexistentPerson_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/people/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotFetchAPerson()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/people/{_reportPersonId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
