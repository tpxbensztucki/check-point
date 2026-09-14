using System.Net;
using System.Net.Http.Json;
using CheckPoint.Api.Auth;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

public class AuditLogEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _nonAdminPersonId;
    private Guid _lineManagerPersonId;
    private Guid _targetPersonId;
    private Guid _otherPersonId;

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
        var nonAdmin = new Person { FullName = "Sam NonAdmin", PracticeId = practice.Id };
        var lineManager = new Person { FullName = "Morgan Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var target = new Person { FullName = "Riley Target", PracticeId = practice.Id };
        var other = new Person { FullName = "Casey Other", PracticeId = practice.Id };
        db.People.AddRange(admin, nonAdmin, lineManager, target, other);
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _nonAdminPersonId = nonAdmin.Id;
        _lineManagerPersonId = lineManager.Id;
        _targetPersonId = target.Id;
        _otherPersonId = other.Id;
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
    public async Task RecordingAView_ThenRecordingAnExport_BothAppearAsDistinctEntries()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var service = new AuditLogService(db);

        await service.RecordViewAsync(_lineManagerPersonId, _targetPersonId);
        await service.RecordExportAsync(_lineManagerPersonId, _targetPersonId);

        using var client = CreateClient(_adminPersonId);
        var response = await client.GetAsync($"/audit-log?personId={_targetPersonId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<List<AuditLogEntryResponse>>(JsonTestOptions.Value);
        Assert.Equal(2, entries!.Count);
        Assert.Contains(entries, e => e.Action == AuditAction.View);
        Assert.Contains(entries, e => e.Action == AuditAction.Export);
        Assert.All(entries, e => Assert.Equal("Riley Target", e.PersonName));
        Assert.All(entries, e => Assert.Equal("Morgan Manager", e.ViewerName));
    }

    [Fact]
    public async Task FilteringByPerson_OnlyReturnsEntriesForThatPerson()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var service = new AuditLogService(db);

        await service.RecordViewAsync(_lineManagerPersonId, _targetPersonId);
        await service.RecordViewAsync(_lineManagerPersonId, _otherPersonId);

        using var client = CreateClient(_adminPersonId);
        var response = await client.GetAsync($"/audit-log?personId={_targetPersonId}");

        var entries = await response.Content.ReadFromJsonAsync<List<AuditLogEntryResponse>>(JsonTestOptions.Value);
        Assert.Single(entries!);
        Assert.Equal(_targetPersonId, entries![0].PersonId);
    }

    [Fact]
    public async Task FilteringByViewer_OnlyReturnsEntriesFromThatViewer()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var service = new AuditLogService(db);

        await service.RecordViewAsync(_lineManagerPersonId, _targetPersonId);
        await service.RecordViewAsync(_adminPersonId, _targetPersonId);

        using var client = CreateClient(_adminPersonId);
        var response = await client.GetAsync($"/audit-log?viewerId={_lineManagerPersonId}");

        var entries = await response.Content.ReadFromJsonAsync<List<AuditLogEntryResponse>>(JsonTestOptions.Value);
        Assert.Single(entries!);
        Assert.Equal(_lineManagerPersonId, entries![0].ViewerId);
    }

    [Fact]
    public async Task FilteringByDateRange_ExcludesEntriesOutsideIt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
        var service = new AuditLogService(db);
        await service.RecordViewAsync(_lineManagerPersonId, _targetPersonId);

        using var client = CreateClient(_adminPersonId);
        var futureFrom = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var response = await client.GetAsync($"/audit-log?from={Uri.EscapeDataString(futureFrom)}");

        var entries = await response.Content.ReadFromJsonAsync<List<AuditLogEntryResponse>>(JsonTestOptions.Value);
        Assert.Empty(entries!);
    }

    [Fact]
    public async Task NonAdmin_CannotViewTheAuditLog()
    {
        using var client = CreateClient(_nonAdminPersonId);

        var response = await client.GetAsync("/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewTheAuditLog()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/audit-log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
