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

public class PersonEndpointsTests : IAsyncLifetime
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

        // Force the host (and its startup migration) to build before seeding.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();

        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var admin = new Person { FullName = "Alex Admin", Roles = [adminRole], PracticeId = Guid.Empty };
        var nonAdmin = new Person { FullName = "Sam NonAdmin", PracticeId = Guid.Empty };

        var department = new Department { Name = "Tech & Data" };
        var practice = new Practice { Name = "Software Engineering", Department = department };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();
        _practiceId = practice.Id;

        admin.PracticeId = practice.Id;
        nonAdmin.PracticeId = practice.Id;
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
    public async Task Admin_CanCreatePerson_DefaultsToEmployedWithNoRolesAndNoLineManager()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var person = await response.Content.ReadFromJsonAsync<PersonResponse>();
        Assert.Equal("Jamie Newhire", person!.FullName);
        Assert.Equal(PersonStatus.Employed, person.Status);
        Assert.Equal(_practiceId, person.PracticeId);
        Assert.Null(person.LineManagerId);
        Assert.Null(person.HeadOfPracticeId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var saved = await db.People.Include(p => p.Roles).SingleAsync(p => p.Id == person.Id);
        Assert.Empty(saved.Roles);
    }

    [Fact]
    public async Task Admin_CanCreatePersonWithLineManagerAndHeadOfPractice()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            "/people",
            new CreatePersonRequest("Jamie Newhire", _practiceId, _adminPersonId, _adminPersonId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var person = await response.Content.ReadFromJsonAsync<PersonResponse>();
        Assert.Equal(_adminPersonId, person!.LineManagerId);
        Assert.Equal(_adminPersonId, person.HeadOfPracticeId);
    }

    [Fact]
    public async Task PersonCannotBeCreatedWithANonexistentPractice()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", Guid.NewGuid(), null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PersonCannotBeCreatedWithANonexistentLineManager()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, Guid.NewGuid(), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotCreatePerson()
    {
        using var client = CreateClient(_nonAdminPersonId);

        var response = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotCreatePerson()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanEditPersonDetailsAndAssignLineManager()
    {
        using var client = CreateClient(_adminPersonId);
        var created = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));
        var person = await created.Content.ReadFromJsonAsync<PersonResponse>();

        var response = await client.PutAsJsonAsync(
            $"/people/{person!.Id}",
            new UpdatePersonRequest("Jamie Renamed", _practiceId, _adminPersonId, _adminPersonId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PersonResponse>();
        Assert.Equal("Jamie Renamed", updated!.FullName);
        Assert.Equal(_adminPersonId, updated.LineManagerId);
        Assert.Equal(_adminPersonId, updated.HeadOfPracticeId);
    }

    [Fact]
    public async Task PersonCannotBeSetAsTheirOwnLineManager()
    {
        using var client = CreateClient(_adminPersonId);
        var created = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));
        var person = await created.Content.ReadFromJsonAsync<PersonResponse>();

        var response = await client.PutAsJsonAsync(
            $"/people/{person!.Id}",
            new UpdatePersonRequest("Jamie Newhire", _practiceId, person.Id, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditingANonexistentPerson_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PutAsJsonAsync(
            $"/people/{Guid.NewGuid()}",
            new UpdatePersonRequest("Jamie Newhire", _practiceId, null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditingAPersonWithANonexistentPractice_ReturnsBadRequest()
    {
        using var client = CreateClient(_adminPersonId);
        var created = await client.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));
        var person = await created.Content.ReadFromJsonAsync<PersonResponse>();

        var response = await client.PutAsJsonAsync(
            $"/people/{person!.Id}",
            new UpdatePersonRequest("Jamie Newhire", Guid.NewGuid(), null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotEditPerson()
    {
        using var adminClient = CreateClient(_adminPersonId);
        var created = await adminClient.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));
        var person = await created.Content.ReadFromJsonAsync<PersonResponse>();

        using var client = CreateClient(_nonAdminPersonId);
        var response = await client.PutAsJsonAsync(
            $"/people/{person!.Id}",
            new UpdatePersonRequest("Jamie Newhire", _practiceId, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotEditPerson()
    {
        using var adminClient = CreateClient(_adminPersonId);
        var created = await adminClient.PostAsJsonAsync(
            "/people", new CreatePersonRequest("Jamie Newhire", _practiceId, null, null));
        var person = await created.Content.ReadFromJsonAsync<PersonResponse>();

        using var client = CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/people/{person!.Id}",
            new UpdatePersonRequest("Jamie Newhire", _practiceId, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
