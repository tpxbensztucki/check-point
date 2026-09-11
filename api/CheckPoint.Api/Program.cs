using CheckPoint.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<CheckPointDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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
// the container itself only binds HTTP.
var runningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (!runningInContainer)
{
    app.UseHttpsRedirection();
}

app.MapHealthChecks("/health");

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
