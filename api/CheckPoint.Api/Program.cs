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
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.Configure<NewStarterCycleOptions>(
    builder.Configuration.GetSection(NewStarterCycleOptions.SectionName));
builder.Services.Configure<GeneralCycleOptions>(
    builder.Configuration.GetSection(GeneralCycleOptions.SectionName));
builder.Services.Configure<RequestDispatchOptions>(
    builder.Configuration.GetSection(RequestDispatchOptions.SectionName));
builder.Services.Configure<FrontendOptions>(
    builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection(SmtpOptions.SectionName));

// Not registered in "Testing" (integration tests) — see the background service's
// own doc comment for why.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<RequestDispatchBackgroundService>();
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
