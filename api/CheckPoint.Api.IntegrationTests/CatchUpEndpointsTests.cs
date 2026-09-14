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

public class CatchUpEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _lineManagerPersonId;
    private Guid _otherLineManagerPersonId;
    private Guid _catchUpId;

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

        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var lineManager = new Person { FullName = "Lee Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var otherLineManager = new Person { FullName = "Other Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        db.People.AddRange(lineManager, otherLineManager);
        await db.SaveChangesAsync();

        var report = new Person { FullName = "Riley Report", PracticeId = practice.Id, LineManagerId = lineManager.Id };
        db.People.Add(report);
        await db.SaveChangesAsync();

        var catchUp = new CatchUp { PersonId = report.Id, CreatedAt = DateTimeOffset.UtcNow };
        db.CatchUps.Add(catchUp);
        report.UnderReviewSince = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        _lineManagerPersonId = lineManager.Id;
        _otherLineManagerPersonId = otherLineManager.Id;
        _catchUpId = catchUp.Id;
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
    public async Task ThePersonsOwnLineManager_CanRecordAnOutcome()
    {
        using var client = CreateClient(_lineManagerPersonId);

        var response = await client.PostAsJsonAsync(
            $"/catch-ups/{_catchUpId}/outcome", new RecordCatchUpOutcomeRequest(CatchUpOutcomeType.NoActionClosed, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catchUp = await response.Content.ReadFromJsonAsync<CatchUpResponse>(JsonTestOptions.Value);
        Assert.Equal(CatchUpStatus.Recorded, catchUp!.Status);
    }

    [Fact]
    public async Task ALineManagerWithNoRelationToThePerson_CannotRecordAnOutcome()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.PostAsJsonAsync(
            $"/catch-ups/{_catchUpId}/outcome", new RecordCatchUpOutcomeRequest(CatchUpOutcomeType.NoActionClosed, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RecordingTwice_ReturnsConflict()
    {
        using var client = CreateClient(_lineManagerPersonId);
        await client.PostAsJsonAsync(
            $"/catch-ups/{_catchUpId}/outcome", new RecordCatchUpOutcomeRequest(CatchUpOutcomeType.NoActionClosed, null));

        var response = await client.PostAsJsonAsync(
            $"/catch-ups/{_catchUpId}/outcome", new RecordCatchUpOutcomeRequest(CatchUpOutcomeType.NoActionClosed, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownCatchUp_ReturnsNotFound()
    {
        using var client = CreateClient(_lineManagerPersonId);

        var response = await client.PostAsJsonAsync(
            $"/catch-ups/{Guid.NewGuid()}/outcome", new RecordCatchUpOutcomeRequest(CatchUpOutcomeType.NoActionClosed, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotRecordAnOutcome()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/catch-ups/{_catchUpId}/outcome", new RecordCatchUpOutcomeRequest(CatchUpOutcomeType.NoActionClosed, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
