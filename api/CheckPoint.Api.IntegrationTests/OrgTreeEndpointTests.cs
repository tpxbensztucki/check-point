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

public class OrgTreeEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private WebApplicationFactory<Program> _factory = null!;

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

        // Force the host (and its startup migration) to build.
        using var scope = _factory.Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<CheckPointDbContext>();
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

    private CheckPointDbContext CreateDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<CheckPointDbContext>();

    private static OrgPersonNode? FindNode(IEnumerable<OrgPersonNode> nodes, Guid id) =>
        nodes.SelectMany(Flatten).SingleOrDefault(n => n.Id == id);

    private static IEnumerable<OrgPersonNode> Flatten(OrgPersonNode node)
    {
        yield return node;
        foreach (var report in node.Reports)
        {
            foreach (var descendant in Flatten(report))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public async Task Admin_SeesTheFullTreeAcrossAllPractices()
    {
        await using var db = CreateDb();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        var practiceA = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var practiceB = new Practice { Name = "Design", Department = new Department { Name = "Tech & Data" } };
        db.Practices.AddRange(practiceA, practiceB);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practiceA.Id, Roles = [adminRole] };
        var managerA = new Person { FullName = "Morgan ManagerA", PracticeId = practiceA.Id, Roles = [lineManagerRole] };
        var managerB = new Person { FullName = "Morgan ManagerB", PracticeId = practiceB.Id, Roles = [lineManagerRole] };
        db.People.AddRange(admin, managerA, managerB);
        await db.SaveChangesAsync();

        var reportA = new Person { FullName = "Riley ReportA", PracticeId = practiceA.Id, LineManagerId = managerA.Id };
        var reportB = new Person { FullName = "Riley ReportB", PracticeId = practiceB.Id, LineManagerId = managerB.Id };
        db.People.AddRange(reportA, reportB);
        await db.SaveChangesAsync();

        using var client = CreateClient(admin.Id);
        var response = await client.GetAsync("/org-tree");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tree = await response.Content.ReadFromJsonAsync<List<OrgPersonNode>>();
        var allNodeIds = tree!.SelectMany(Flatten).Select(n => n.Id).ToHashSet();
        Assert.Contains(admin.Id, allNodeIds);
        Assert.Contains(managerA.Id, allNodeIds);
        Assert.Contains(managerB.Id, allNodeIds);
        Assert.Contains(reportA.Id, allNodeIds);
        Assert.Contains(reportB.Id, allNodeIds);

        var managerANode = FindNode(tree!, managerA.Id)!;
        Assert.Contains(managerANode.Reports, r => r.Id == reportA.Id);
    }

    [Fact]
    public async Task PracticeLead_SeesOnlyTheirOwnPractice_WithCrossPracticeManagerExcludedButReportShown()
    {
        await using var db = CreateDb();
        var practiceLeadRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        var practiceA = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var practiceB = new Practice { Name = "Design", Department = new Department { Name = "Tech & Data" } };
        db.Practices.AddRange(practiceA, practiceB);
        await db.SaveChangesAsync();

        var leadA = new Person { FullName = "Lee LeadA", PracticeId = practiceA.Id, Roles = [practiceLeadRole] };
        var managerB = new Person { FullName = "Morgan ManagerB", PracticeId = practiceB.Id, Roles = [lineManagerRole] };
        db.People.AddRange(leadA, managerB);
        await db.SaveChangesAsync();

        practiceA.PracticeLeadId = leadA.Id;
        await db.SaveChangesAsync();

        // Tagged to Practice A but managed by someone in Practice B — should
        // appear (flagged Orphaned), but managerB should not appear in this tree.
        var crossPracticeReport = new Person
        {
            FullName = "Riley CrossReport", PracticeId = practiceA.Id, LineManagerId = managerB.Id,
        };
        db.People.Add(crossPracticeReport);
        await db.SaveChangesAsync();

        using var client = CreateClient(leadA.Id);
        var response = await client.GetAsync("/org-tree");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tree = await response.Content.ReadFromJsonAsync<List<OrgPersonNode>>();
        var allNodeIds = tree!.SelectMany(Flatten).Select(n => n.Id).ToHashSet();

        Assert.Contains(crossPracticeReport.Id, allNodeIds);
        Assert.DoesNotContain(managerB.Id, allNodeIds);

        var reportNode = FindNode(tree!, crossPracticeReport.Id)!;
        Assert.True(reportNode.IsOrphaned);
    }

    [Fact]
    public async Task LineManager_SeesThemselvesAndExactlyTheirDirectReports_NotGrandchildren()
    {
        await using var db = CreateDb();
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var manager = new Person { FullName = "Morgan Manager", PracticeId = practice.Id, Roles = [lineManagerRole] };
        db.People.Add(manager);
        await db.SaveChangesAsync();

        var report = new Person { FullName = "Riley Report", PracticeId = practice.Id, LineManagerId = manager.Id, Roles = [lineManagerRole] };
        db.People.Add(report);
        await db.SaveChangesAsync();

        var grandchild = new Person { FullName = "Grady Grandchild", PracticeId = practice.Id, LineManagerId = report.Id };
        db.People.Add(grandchild);
        await db.SaveChangesAsync();

        using var client = CreateClient(manager.Id);
        var response = await client.GetAsync("/org-tree");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tree = await response.Content.ReadFromJsonAsync<List<OrgPersonNode>>();
        var allNodeIds = tree!.SelectMany(Flatten).Select(n => n.Id).ToHashSet();

        Assert.Contains(manager.Id, allNodeIds);
        Assert.Contains(report.Id, allNodeIds);
        Assert.DoesNotContain(grandchild.Id, allNodeIds);
    }

    [Fact]
    public async Task PersonHoldingMultipleRoles_SeesTheCombinedScope()
    {
        await using var db = CreateDb();
        var practiceLeadRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        var practiceA = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        var practiceB = new Practice { Name = "Design", Department = new Department { Name = "Tech & Data" } };
        db.Practices.AddRange(practiceA, practiceB);
        await db.SaveChangesAsync();

        var viewer = new Person
        {
            FullName = "Robin ViewerBoth", PracticeId = practiceA.Id, Roles = [practiceLeadRole, lineManagerRole],
        };
        db.People.Add(viewer);
        await db.SaveChangesAsync();

        practiceA.PracticeLeadId = viewer.Id;
        await db.SaveChangesAsync();

        var practiceAPerson = new Person { FullName = "Ali PracticeAPerson", PracticeId = practiceA.Id };
        db.People.Add(practiceAPerson);
        await db.SaveChangesAsync();

        // Not in Practice A, but a direct report of the viewer via the Line
        // Manager side of their scope.
        var crossPracticeReport = new Person
        {
            FullName = "Cameron CrossReport", PracticeId = practiceB.Id, LineManagerId = viewer.Id,
        };
        db.People.Add(crossPracticeReport);
        await db.SaveChangesAsync();

        using var client = CreateClient(viewer.Id);
        var response = await client.GetAsync("/org-tree");

        var tree = await response.Content.ReadFromJsonAsync<List<OrgPersonNode>>();
        var allNodeIds = tree!.SelectMany(Flatten).Select(n => n.Id).ToHashSet();

        Assert.Contains(practiceAPerson.Id, allNodeIds);
        Assert.Contains(crossPracticeReport.Id, allNodeIds);
    }

    [Fact]
    public async Task PersonWithNoRelevantRole_SeesAnEmptyTree()
    {
        await using var db = CreateDb();
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var noRolePerson = new Person { FullName = "Noel NoRole", PracticeId = practice.Id };
        db.People.Add(noRolePerson);
        await db.SaveChangesAsync();

        using var client = CreateClient(noRolePerson.Id);
        var response = await client.GetAsync("/org-tree");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tree = await response.Content.ReadFromJsonAsync<List<OrgPersonNode>>();
        Assert.Empty(tree!);
    }

    [Fact]
    public async Task AManagerCycleInTheData_DoesNotCauseAnInfiniteLoop()
    {
        await using var db = CreateDb();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var practice = new Practice { Name = "Software Engineering", Department = new Department { Name = "Tech & Data" } };
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var admin = new Person { FullName = "Alex Admin", PracticeId = practice.Id, Roles = [adminRole] };
        var personA = new Person { FullName = "Ali PersonA", PracticeId = practice.Id };
        var personB = new Person { FullName = "Blake PersonB", PracticeId = practice.Id };
        db.People.AddRange(admin, personA, personB);
        await db.SaveChangesAsync();

        // Deliberately construct a cycle directly via the DB, bypassing the
        // self-as-own-manager check the Update endpoint enforces.
        personA.LineManagerId = personB.Id;
        personB.LineManagerId = personA.Id;
        await db.SaveChangesAsync();

        using var client = CreateClient(admin.Id);
        var response = await client.GetAsync("/org-tree");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedCaller_CannotViewTheOrgTree()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/org-tree");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
