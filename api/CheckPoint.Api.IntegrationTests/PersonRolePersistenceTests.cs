using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace CheckPoint.Api.IntegrationTests;

// Exercises the real migration (including seed data) and the persisted many-to-many
// relationship against a real Postgres instance.
public class PersonRolePersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private CheckPointDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CheckPointDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new CheckPointDbContext(options);
    }

    [Fact]
    public async Task Migration_SeedsExactlyTheThreeValidRoles()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var roleNames = await context.Roles.Select(r => r.Name).ToListAsync();

        Assert.Equal(
            new[] { RoleNames.Admin, RoleNames.LineManager, RoleNames.PracticeLead }.OrderBy(n => n),
            roleNames.OrderBy(n => n));
    }

    [Fact]
    public async Task Person_CanBeAssignedMultipleRoles_AndTheyPersist()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            var practiceLead = await setup.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);
            var lineManager = await setup.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

            setup.People.Add(new Person
            {
                FullName = "Alex Doe",
                Roles = [practiceLead, lineManager],
            });
            await setup.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var person = await verify.People
            .Include(p => p.Roles)
            .SingleAsync(p => p.FullName == "Alex Doe");

        Assert.Equal(2, person.Roles.Count);
        Assert.Contains(person.Roles, r => r.Name == RoleNames.PracticeLead);
        Assert.Contains(person.Roles, r => r.Name == RoleNames.LineManager);
    }

    [Fact]
    public async Task RemovingARoleFromOnePerson_DoesNotAffectAnotherPersonWithTheSameRole()
    {
        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();

            var admin = await setup.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
            setup.People.Add(new Person { FullName = "Alex Doe", Roles = [admin] });
            setup.People.Add(new Person { FullName = "Sam Doe", Roles = [admin] });
            await setup.SaveChangesAsync();
        }

        await using (var mutate = CreateContext())
        {
            var alex = await mutate.People
                .Include(p => p.Roles)
                .SingleAsync(p => p.FullName == "Alex Doe");
            alex.Roles.Clear();
            await mutate.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var sam = await verify.People
            .Include(p => p.Roles)
            .SingleAsync(p => p.FullName == "Sam Doe");

        Assert.Contains(sam.Roles, r => r.Name == RoleNames.Admin);
    }
}
