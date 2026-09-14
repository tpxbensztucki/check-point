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

public class PocEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _adminPersonId;
    private Guid _practiceLeadPersonId;
    private Guid _otherPracticeLeadPersonId;
    private Guid _lineManagerPersonId;
    private Guid _otherLineManagerPersonId;
    private Guid _targetPersonId;
    private Guid _projectId;

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
        var practiceLeadRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var otherPractice = new Practice { Name = "Design", Department = new Department { Name = "Tech & Data" } };
        db.Practices.AddRange(practice, otherPractice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        var practiceLead = new Person { FullName = "Lee Lead", PracticeId = practice.Id, Roles = [practiceLeadRole] };
        var otherPracticeLead = new Person { FullName = "Robin OtherLead", PracticeId = otherPractice.Id, Roles = [practiceLeadRole] };
        var lineManager = new Person { FullName = "Morgan Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        var otherLineManager = new Person { FullName = "Casey OtherManager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        db.People.AddRange(admin, practiceLead, otherPracticeLead, lineManager, otherLineManager);
        await db.SaveChangesAsync();

        practice.PracticeLeadId = practiceLead.Id;
        otherPractice.PracticeLeadId = otherPracticeLead.Id;
        await db.SaveChangesAsync();

        var target = new Person { FullName = "Riley Target", PracticeId = practice.Id, LineManagerId = lineManager.Id };
        db.People.Add(target);
        await db.SaveChangesAsync();

        var project = new Project { Name = "Website Revamp" };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        db.ProjectMemberships.Add(new ProjectMembership { ProjectId = project.Id, PersonId = target.Id, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        _adminPersonId = admin.Id;
        _practiceLeadPersonId = practiceLead.Id;
        _otherPracticeLeadPersonId = otherPracticeLead.Id;
        _lineManagerPersonId = lineManager.Id;
        _otherLineManagerPersonId = otherLineManager.Id;
        _targetPersonId = target.Id;
        _projectId = project.Id;
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

    private string PocsPath() => $"/projects/{_projectId}/people/{_targetPersonId}/pocs";

    private async Task<Guid> AssignPocAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));
        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        return result!.Pocs[^1].Id;
    }

    [Fact]
    public async Task Admin_CanEditAPoc()
    {
        using var client = CreateClient(_adminPersonId);
        var pocId = await AssignPocAsync(client);

        var response = await client.PutAsJsonAsync(
            $"{PocsPath()}/{pocId}",
            new CreatePocRequest("Jamie Corrected", "jamie.new@example.com", PocRelationship.Internal, PocRole.Dm));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        var updated = result!.Pocs.Single(p => p.Id == pocId);
        Assert.Equal("Jamie Corrected", updated.Name);
        Assert.Equal("jamie.new@example.com", updated.Email);
        Assert.Equal(PocRelationship.Internal, updated.Relationship);
        Assert.Equal(PocRole.Dm, updated.Role);
    }

    [Fact]
    public async Task EditingAPocWithAnInvalidEmail_IsRejected()
    {
        using var client = CreateClient(_adminPersonId);
        var pocId = await AssignPocAsync(client);

        var response = await client.PutAsJsonAsync(
            $"{PocsPath()}/{pocId}",
            new CreatePocRequest("Jamie Tech", "not-an-email", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditingANonexistentPoc_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PutAsJsonAsync(
            $"{PocsPath()}/{Guid.NewGuid()}",
            new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LineManager_CannotEditAPocForSomeoneWhoIsNotTheirReport()
    {
        using var admin = CreateClient(_adminPersonId);
        var pocId = await AssignPocAsync(admin);

        using var client = CreateClient(_otherLineManagerPersonId);
        var response = await client.PutAsJsonAsync(
            $"{PocsPath()}/{pocId}",
            new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanRemoveAPoc()
    {
        using var client = CreateClient(_adminPersonId);
        var pocId = await AssignPocAsync(client);

        var response = await client.DeleteAsync($"{PocsPath()}/{pocId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        Assert.Empty(result!.Pocs);
        Assert.Contains(PocRole.Tech, result.MissingStandardRoles);
    }

    [Fact]
    public async Task RemovingANonexistentPoc_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.DeleteAsync($"{PocsPath()}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemovingAPocFromOneProject_DoesNotAffectTheSamePersonsPocOnAnotherProject()
    {
        using var adminClient = CreateClient(_adminPersonId);
        var otherProject = await (await adminClient.PostAsJsonAsync("/projects", new CreateProjectRequest("Second Project")))
            .Content.ReadFromJsonAsync<ProjectResponse>(JsonTestOptions.Value);
        await adminClient.PostAsJsonAsync(
            $"/projects/{otherProject!.Id}/people", new AddPersonToProjectRequest(_targetPersonId));

        var pocIdOnFirstProject = await AssignPocAsync(adminClient);
        var otherProjectPocsPath = $"/projects/{otherProject.Id}/people/{_targetPersonId}/pocs";
        await adminClient.PostAsJsonAsync(
            otherProjectPocsPath, new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        await adminClient.DeleteAsync($"{PocsPath()}/{pocIdOnFirstProject}");

        var otherProjectPocs = await adminClient.GetFromJsonAsync<ProjectMembershipPocsResponse>(
            otherProjectPocsPath, JsonTestOptions.Value);
        Assert.Single(otherProjectPocs!.Pocs);
    }

    [Fact]
    public async Task NonAdmin_WithNoRelationToTarget_CannotRemoveAPoc()
    {
        using var admin = CreateClient(_adminPersonId);
        var pocId = await AssignPocAsync(admin);

        using var client = CreateClient(_otherLineManagerPersonId);
        var response = await client.DeleteAsync($"{PocsPath()}/{pocId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanAssignAPoc()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        Assert.Single(result!.Pocs);
        Assert.Equal("Jamie Tech", result.Pocs[0].Name);
    }

    [Fact]
    public async Task IncompleteStandardSet_FlagsTheMissingRoles()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Dana Dm", "dana@example.com", PocRelationship.Internal, PocRole.Dm));

        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        Assert.Equal(2, result!.Pocs.Count);
        Assert.Equal([PocRole.Other], result.MissingStandardRoles);
    }

    [Fact]
    public async Task CompleteStandardSet_HasNoMissingRoles()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsJsonAsync(PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));
        await client.PostAsJsonAsync(PocsPath(), new CreatePocRequest("Dana Dm", "dana@example.com", PocRelationship.Internal, PocRole.Dm));

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Sam Other", "sam@example.com", PocRelationship.External, PocRole.Other));

        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        Assert.Empty(result!.MissingStandardRoles);
    }

    // CBLT-255: the completeness indicator uses the Admin-configured target
    // count per role, not a fixed "at least one" assumption.
    [Fact]
    public async Task RaisingTheTechTarget_FlagsAProjectWithOnlyOneTechPocAsIncomplete()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PutAsJsonAsync(
            "/admin/settings",
            new UpdateAdminSettingsRequest(
                NewStarterIntervalWeeks: [2, 4, 8],
                GeneralCycleSkipThresholdWeeks: 4,
                AutomaticRequestSendingEnabled: true,
                TargetTechPocCount: 2,
                TargetDmPocCount: 1,
                TargetOtherPocCount: 1));

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        Assert.Single(result!.Pocs);
        Assert.Contains(PocRole.Tech, result.MissingStandardRoles);
    }

    [Fact]
    public async Task AssigningMoreThanTheConfiguredTarget_IsStillAllowedAndNotFlaggedMissing()
    {
        using var client = CreateClient(_adminPersonId);
        await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));
        await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Dana Dm", "dana@example.com", PocRelationship.Internal, PocRole.Dm));
        await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Sam Other", "sam@example.com", PocRelationship.External, PocRole.Other));

        // A second Tech POC, on top of the default target of 1 — the target
        // is not a hard cap.
        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Alex SecondTech", "alex@example.com", PocRelationship.Internal, PocRole.Tech));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectMembershipPocsResponse>(JsonTestOptions.Value);
        Assert.Equal(4, result!.Pocs.Count);
        Assert.Empty(result.MissingStandardRoles);
    }

    [Fact]
    public async Task AnInvalidEmail_IsRejected()
    {
        using var client = CreateClient(_lineManagerPersonId);

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "not-an-email", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LineManager_CanAssignAPocForTheirOwnReport()
    {
        using var client = CreateClient(_lineManagerPersonId);

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task LineManager_CannotAssignAPocForSomeoneWhoIsNotTheirReport()
    {
        using var client = CreateClient(_otherLineManagerPersonId);

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PracticeLead_CanAssignAPocForSomeoneInTheirPractice()
    {
        using var client = CreateClient(_practiceLeadPersonId);

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PracticeLead_CannotAssignAPocForSomeoneOutsideTheirPractice()
    {
        using var client = CreateClient(_otherPracticeLeadPersonId);

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AssigningToANonMember_ReturnsNotFound()
    {
        using var client = CreateClient(_adminPersonId);

        var response = await client.PostAsJsonAsync(
            $"/projects/{_projectId}/people/{Guid.NewGuid()}/pocs",
            new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PocsAreScopedToOneMembership_NotSharedAcrossProjects()
    {
        using var adminClient = CreateClient(_adminPersonId);
        var otherProject = await (await adminClient.PostAsJsonAsync("/projects", new CreateProjectRequest("Second Project")))
            .Content.ReadFromJsonAsync<ProjectResponse>(JsonTestOptions.Value);
        await adminClient.PostAsJsonAsync(
            $"/projects/{otherProject!.Id}/people", new AddPersonToProjectRequest(_targetPersonId));

        await adminClient.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        var otherProjectPocs = await adminClient.GetFromJsonAsync<ProjectMembershipPocsResponse>(
            $"/projects/{otherProject.Id}/people/{_targetPersonId}/pocs", JsonTestOptions.Value);

        Assert.Empty(otherProjectPocs!.Pocs);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotAssignAPoc()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            PocsPath(), new CreatePocRequest("Jamie Tech", "jamie@example.com", PocRelationship.Client, PocRole.Tech));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
