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

public class AdminSettingsEndpointsTests : IAsyncLifetime
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

    private static UpdateAdminSettingsRequest ValidRequest() =>
        new(
            NewStarterIntervalWeeks: [3, 6, 10],
            GeneralCycleSkipThresholdWeeks: 6,
            AutomaticRequestSendingEnabled: false,
            TargetTechPocCount: 2,
            TargetDmPocCount: 1,
            TargetOtherPocCount: 1);

    [Fact]
    public async Task Admin_SeesDefaultSettingsOnAFreshDatabase()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.GetAsync("/admin/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<AdminSettingsResponse>(JsonTestOptions.Value);
        Assert.Equal([2, 4, 8], settings!.NewStarterIntervalWeeks);
        Assert.Equal(4, settings.GeneralCycleSkipThresholdWeeks);
        Assert.True(settings.AutomaticRequestSendingEnabled);
        Assert.Equal(1, settings.TargetTechPocCount);
        Assert.Equal(1, settings.TargetDmPocCount);
        Assert.Equal(1, settings.TargetOtherPocCount);
    }

    [Fact]
    public async Task Admin_CanUpdateSettingsAndSeeTheChangePersisted()
    {
        using var client = CreateClient(_adminPersonId);

        var updateResponse = await client.PutAsJsonAsync("/admin/settings", ValidRequest());

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<AdminSettingsResponse>(JsonTestOptions.Value);
        Assert.Equal([3, 6, 10], updated!.NewStarterIntervalWeeks);
        Assert.Equal(6, updated.GeneralCycleSkipThresholdWeeks);
        Assert.False(updated.AutomaticRequestSendingEnabled);
        Assert.Equal(2, updated.TargetTechPocCount);

        var getResponse = await client.GetAsync("/admin/settings");
        var reread = await getResponse.Content.ReadFromJsonAsync<AdminSettingsResponse>(JsonTestOptions.Value);
        Assert.Equal(updated, reread);
    }

    [Fact]
    public async Task UpdatingWithEmptyNewStarterIntervals_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PutAsJsonAsync(
            "/admin/settings", ValidRequest() with { NewStarterIntervalWeeks = [] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatingWithAZeroSkipThreshold_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PutAsJsonAsync(
            "/admin/settings", ValidRequest() with { GeneralCycleSkipThresholdWeeks = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatingWithANegativePocTarget_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PutAsJsonAsync(
            "/admin/settings", ValidRequest() with { TargetTechPocCount = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotViewSettings()
    {
        using var client = CreateClient(_nonAdminPersonId);

        var response = await client.GetAsync("/admin/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotUpdateSettings()
    {
        using var client = CreateClient(_nonAdminPersonId);

        var response = await client.PutAsJsonAsync("/admin/settings", ValidRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewSettings()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/admin/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
