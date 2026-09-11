using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Example integration test — boots the real API against a real (Testcontainers)
// Postgres instance. Building the WebApplicationFactory runs the app's startup
// migration logic against this container, so the /health check here exercises the
// full API + database flow, not just an in-memory app.
public class HealthCheckTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }
}
