using System.Net;
using System.Net.Http.Json;
using CheckPoint.Api.Auth;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Covers CBLT-219: visibility of a Practice's people follows Practice tags, and
// the Orphaned flag it exposes.
public class PracticePeopleEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _practiceALeadId;
    private Guid _practiceBLeadId;
    private Guid _practiceAId;
    private Guid _practiceBId;

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
        var practiceLeadRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);

        var practiceA = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var practiceB = new Practice { Name = "Design", Department = new Department { Name = "Tech & Data" } };
        db.Practices.AddRange(practiceA, practiceB);
        await db.SaveChangesAsync();
        _practiceAId = practiceA.Id;
        _practiceBId = practiceB.Id;

        var admin = new Person { FullName = "Alex Admin", PracticeId = practiceA.Id, Roles = [adminRole] };
        var practiceALead = new Person { FullName = "Lee Lead A", PracticeId = practiceA.Id, Roles = [practiceLeadRole] };
        var practiceBLead = new Person { FullName = "Lee Lead B", PracticeId = practiceB.Id, Roles = [practiceLeadRole] };
        db.People.AddRange(admin, practiceALead, practiceBLead);
        await db.SaveChangesAsync();

        practiceA.PracticeLeadId = practiceALead.Id;
        practiceB.PracticeLeadId = practiceBLead.Id;
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _practiceALeadId = practiceALead.Id;
        _practiceBLeadId = practiceBLead.Id;
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

    private async Task<Guid> AddPersonAsync(Guid practiceId, Guid? lineManagerId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var person = new Person { FullName = "Riley Report", PracticeId = practiceId, LineManagerId = lineManagerId };
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    [Fact]
    public async Task PersonWithNoLineManager_IsFlaggedOrphaned()
    {
        var personId = await AddPersonAsync(_practiceAId);
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/practices/{_practiceAId}/people");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var people = await response.Content.ReadFromJsonAsync<List<PracticePersonResponse>>();
        var person = people!.Single(p => p.Id == personId);
        Assert.True(person.IsOrphaned);
    }

    [Fact]
    public async Task PersonWhoseLineManagerIsInADifferentPractice_IsFlaggedOrphaned_AndTheLineManagerIsExcludedFromTheTree()
    {
        var personId = await AddPersonAsync(_practiceAId, lineManagerId: _practiceBLeadId);
        using var client = CreateClient(_practiceALeadId);

        var response = await client.GetAsync($"/practices/{_practiceAId}/people");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var people = await response.Content.ReadFromJsonAsync<List<PracticePersonResponse>>();
        var person = people!.Single(p => p.Id == personId);
        Assert.True(person.IsOrphaned);
        Assert.DoesNotContain(people!, p => p.Id == _practiceBLeadId);
    }

    [Fact]
    public async Task PersonWhoseLineManagerIsInTheSamePractice_IsNotOrphaned()
    {
        var personId = await AddPersonAsync(_practiceAId, lineManagerId: _practiceALeadId);
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/practices/{_practiceAId}/people");

        var people = await response.Content.ReadFromJsonAsync<List<PracticePersonResponse>>();
        var person = people!.Single(p => p.Id == personId);
        Assert.False(person.IsOrphaned);
    }

    [Fact]
    public async Task Admin_CanViewAnyPracticesPeople()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/practices/{_practiceBId}/people");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PracticeLead_CannotViewADifferentPracticesPeople()
    {
        using var client = CreateClient(_practiceALeadId);

        var response = await client.GetAsync($"/practices/{_practiceBId}/people");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ViewingANonexistentPractice_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync($"/practices/{Guid.NewGuid()}/people");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewAPracticesPeople()
    {
        using var client = CreateClient();

        var response = await client.GetAsync($"/practices/{_practiceAId}/people");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
