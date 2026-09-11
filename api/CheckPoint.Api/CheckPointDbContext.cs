using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api;

public class CheckPointDbContext(DbContextOptions<CheckPointDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<MagicLink> MagicLinks => Set<MagicLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The set of valid roles is closed (spec Section 2) — seed it via migration
        // rather than letting application code create Role rows.
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = RoleNames.Admin },
            new Role { Id = 2, Name = RoleNames.PracticeLead },
            new Role { Id = 3, Name = RoleNames.LineManager });

        // Tokens must be unguessable and unique to look up a link by token alone.
        modelBuilder.Entity<MagicLink>().HasIndex(l => l.Token).IsUnique();
    }
}
