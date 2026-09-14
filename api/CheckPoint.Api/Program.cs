using System.Text.Json.Serialization;
using CheckPoint.Api;
using CheckPoint.Api.Auth;
using CheckPoint.Api.Endpoints;
using CheckPoint.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// Every enum in every contract (ProjectStatus, PersonStatus,
// PocResponseStatus, CatchUpStatus, CatchUpTriggerSource, PocRelationship,
// PocRole, CatchUpOutcomeType, ...) was, without this, silently serialized
// as its raw underlying integer — the .NET default — even though every
// frontend TypeScript type across every dashboard/admin screen expects the
// member's name as a string (e.g. `'Active'`, `'Pending'`). Found while
// smoke-testing CBLT-307's POC creation form by hand: the backend rejected
// a real request body sending `"relationship":"Internal"` because it only
// accepted a number. This fixes both directions at once — request bodies
// carrying an enum by name, and every existing response that had been
// silently sending numbers the frontend's string comparisons never matched
// (e.g. `project.status === 'Active'`, `entry.status === 'Pending'').
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<CheckPointDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<MagicLinkService>();
builder.Services.AddScoped<DepartmentService>();
builder.Services.AddScoped<PersonService>();
builder.Services.AddScoped<OrgTreeService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<PocService>();
builder.Services.AddScoped<FeedbackCycleService>();
builder.Services.AddScoped<FeedbackSubmissionService>();
builder.Services.AddScoped<RequestDispatchService>();
builder.Services.AddScoped<LmNotificationDispatchService>();
builder.Services.AddScoped<PocResponseHistoryService>();
builder.Services.AddScoped<CatchUpService>();
builder.Services.AddScoped<DevPersonDirectoryService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AdminSettingsService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<RequestDispatchOptions>(
    builder.Configuration.GetSection(RequestDispatchOptions.SectionName));
builder.Services.Configure<FrontendOptions>(
    builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection(SmtpOptions.SectionName));

// The frontend and API are always served from different origins (different
// ports locally via Docker Compose, separate Container Apps once deployed) —
// the browser calls the API directly, with no reverse proxy in between, so
// this has always been needed; it just went unnoticed until CBLT-304 added
// the first frontend screen that actually calls an endpoint from a real
// browser (every guest-flow test stubs fetch directly and never exercises
// real CORS enforcement). Reuses FrontendOptions.BaseUrl — the frontend's own
// origin is already config-driven for the magic-link emails, so there's no
// second setting to keep in sync.
var frontendBaseUrl = builder.Configuration["Frontend:BaseUrl"] ?? new FrontendOptions().BaseUrl;
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(frontendBaseUrl).AllowAnyHeader().AllowAnyMethod());
});

// Not registered in "Testing" (integration tests) — see the background service's
// own doc comment for why.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<RequestDispatchBackgroundService>();
    builder.Services.AddHostedService<LmNotificationDispatchBackgroundService>();
}

// DevPersonAuthenticationHandler is a stand-in for real sign-in until CBLT-211 (AD
// SSO) exists — see its own doc comment. It must never run in Production, so the
// core auth services are always registered (UseAuthentication/UseAuthorization need
// them present regardless of environment), but the scheme itself -- and making it
// the default -- is only added outside Production. In Production, with no scheme
// registered, everyone is anonymous and [Authorize]-protected endpoints reject
// every request (fail closed) until real SSO is wired up.
var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    if (!builder.Environment.IsProduction())
    {
        options.DefaultScheme = DevPersonAuthenticationHandler.SchemeName;
        options.DefaultAuthenticateScheme = DevPersonAuthenticationHandler.SchemeName;
        options.DefaultChallengeScheme = DevPersonAuthenticationHandler.SchemeName;
    }
});

if (!builder.Environment.IsProduction())
{
    authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevPersonAuthenticationHandler>(
        DevPersonAuthenticationHandler.SchemeName, _ => { });
}

builder.Services.AddAuthorization();

var app = builder.Build();

// Applies any pending EF Core migrations on startup so a fresh `docker compose up`
// (or local run) always ends with an up-to-date schema, no manual step required.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
    db.Database.Migrate();

    // Dev/local-only seed data (one Person per role) — see DevDataSeeder's own
    // doc comment. Deliberately gated to Development specifically (not just
    // "not Production" like the dev auth scheme/endpoint below) — integration
    // tests run in a "Testing" environment against a fresh database per test
    // class and assert on an otherwise-empty People table, so seeding there
    // would corrupt every test's fixture.
    if (app.Environment.IsDevelopment())
    {
        await DevDataSeeder.SeedAsync(db);
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The official ASP.NET Core container images set this automatically. Skip HTTPS
// redirection there since Azure Container Apps terminates TLS at the ingress and
// the container itself only binds HTTP. Also skip outside plain local dev (e.g. in
// integration tests via WebApplicationFactory), where nothing binds an HTTPS port.
var runningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (!runningInContainer && app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapDepartmentEndpoints();
app.MapPersonEndpoints();
app.MapOrgTreeEndpoints();
app.MapProjectEndpoints();
app.MapPocEndpoints();
app.MapMagicLinkEndpoints();
app.MapFeedbackRequestEndpoints();
app.MapCatchUpEndpoints();
app.MapDashboardEndpoints();
app.MapAdminSettingsEndpoints();

// Dev-only, unauthenticated — see DevEndpoints.cs. Same environment gate as
// DevPersonAuthenticationHandler's own registration above.
if (!app.Environment.IsProduction())
{
    app.MapDevEndpoints();
}

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// Makes the implicit top-level-statements Program class public so
// WebApplicationFactory<Program> can be used from the integration test project.
public partial class Program;
