using CheckPoint.Api.Auth;
using CheckPoint.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Shared fixture for the integration test suite — see the "Known test
// brittleness" note in CLAUDE.md. Every test file used to independently boot
// its own PostgreSqlContainer and hand-roll CreateContext()/CreateClient
// (personId), which is what made the AdminSettingsService and enum-JSON
// migrations (CBLT-252/253/254 and the JsonStringEnumConverter fix) each
// require dozens of near-identical mechanical edits. New test files should
// derive from this rather than repeating that setup; the ~32 not yet
// migrated will move over opportunistically.
//
// Supports both styles found in the suite: tests that talk to
// CheckPointDbContext/services directly (CreateContext(), Time), and tests
// that go through the real HTTP pipeline (CreateClient()). Only the first
// caller of CreateClient() pays for spinning up the WebApplicationFactory.
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program>? _factory;

    // Fixed start time shared by every FakeTimeProvider-based test in the
    // suite so far; subclasses that don't need it simply never touch it.
    protected FakeTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    public virtual async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Applying migrations here (rather than relying solely on the API's
        // own startup migration, which only runs once CreateClient() forces
        // the WebApplicationFactory's host to build) means CreateContext()
        // works immediately for tests that never touch the HTTP pipeline.
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public virtual async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    protected CheckPointDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CheckPointDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new CheckPointDbContext(options);
    }

    // Lazily built so tests that only ever use CreateContext() don't pay for
    // standing up the full host. Subclasses that need to seed data through
    // the DI container directly (rather than via CreateContext()) can reach
    // it as Factory.Services.CreateScope()....
    protected WebApplicationFactory<Program> Factory => _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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

    protected HttpClient CreateClient(Guid? actingAsPersonId = null)
    {
        var client = Factory.CreateClient();
        if (actingAsPersonId is { } personId)
        {
            client.DefaultRequestHeaders.Add(DevPersonAuthenticationHandler.PersonIdHeader, personId.ToString());
        }

        return client;
    }

    // The one dependency every direct-service-construction test needed
    // (CBLT-252/253/254 replaced IOptions<T> params with this across the
    // board) — genuinely identical everywhere it's constructed. Bespoke
    // service wiring beyond this stays in each test file.
    protected AdminSettingsService CreateAdminSettingsService(CheckPointDbContext context) => new(context);

    // RequestDispatchService's full constructor was byte-for-byte identical
    // across every service-style test file that needed one.
    protected RequestDispatchService CreateDispatchService(CheckPointDbContext context, IEmailSender emailSender) =>
        new(
            context,
            Time,
            emailSender,
            new MagicLinkService(context, Time),
            CreateAdminSettingsService(context),
            Options.Create(new FrontendOptions()));
}
