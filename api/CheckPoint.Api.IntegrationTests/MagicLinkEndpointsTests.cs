using System.Net;
using System.Net.Http.Json;
using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Guest-facing — unlike every other endpoint test in this project, none of these
// requests carry a DevPersonId header, since a guest never signs in (spec Section 9).
public class MagicLinkEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

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
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(_time));
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // PocId is a real FK (CBLT-302) — every link needs an actual Poc row, even
    // though FeedbackRequestId stays a bare, unconstrained Guid.
    private async Task<Guid> CreatePocAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var project = new Project { Name = "Website Revamp" };
        var person = new Person { FullName = "Riley Reviewee", Practice = practice };
        db.Practices.Add(practice);
        db.Projects.Add(project);
        db.People.Add(person);
        await db.SaveChangesAsync();

        var membership = new ProjectMembership { ProjectId = project.Id, PersonId = person.Id, JoinedAt = DateTimeOffset.UtcNow };
        db.ProjectMemberships.Add(membership);
        await db.SaveChangesAsync();

        var poc = new Poc
        {
            ProjectMembershipId = membership.Id,
            Name = "Jamie POC",
            Email = "jamie@example.com",
            Relationship = PocRelationship.Internal,
            Role = PocRole.Tech,
        };
        db.Pocs.Add(poc);
        await db.SaveChangesAsync();
        return poc.Id;
    }

    private async Task<string> IssueLinkAsync(Guid feedbackRequestId)
    {
        var pocId = await CreatePocAsync();
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MagicLinkService>();
        var link = await service.IssueAsync(feedbackRequestId, pocId);
        return link.Token;
    }

    [Fact]
    public async Task AValidUnexpiredUnusedLink_ReturnsItsFeedbackRequestId()
    {
        var feedbackRequestId = Guid.NewGuid();
        var token = await IssueLinkAsync(feedbackRequestId);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/magic-links/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MagicLinkViewResponse>();
        Assert.Equal(feedbackRequestId, body!.FeedbackRequestId);
    }

    [Fact]
    public async Task AnExpiredLink_ReturnsGone()
    {
        var token = await IssueLinkAsync(Guid.NewGuid());
        _time.Advance(MagicLinkService.ValidityPeriod + TimeSpan.FromDays(1));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/magic-links/{token}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task AnAlreadyUsedLink_ReturnsConflict()
    {
        var token = await IssueLinkAsync(Guid.NewGuid());
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<MagicLinkService>();
            await service.ConsumeAsync(token);
        }

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/magic-links/{token}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownToken_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/magic-links/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoAuthenticationHeaderIsRequired()
    {
        var token = await IssueLinkAsync(Guid.NewGuid());

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/magic-links/{token}");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
