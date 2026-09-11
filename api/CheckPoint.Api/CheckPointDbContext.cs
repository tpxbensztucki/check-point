using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api;

public class CheckPointDbContext(DbContextOptions<CheckPointDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<MagicLink> MagicLinks => Set<MagicLink>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Practice> Practices => Set<Practice>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();
    public DbSet<Poc> Pocs => Set<Poc>();
    public DbSet<FeedbackRequest> FeedbackRequests => Set<FeedbackRequest>();

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

        // Restrict rather than cascade on Person's self-referencing FKs and its
        // required Practice FK — deleting a Practice or a line manager must never
        // silently delete the People that reference them.
        modelBuilder.Entity<Person>()
            .HasOne(p => p.Practice)
            .WithMany(pr => pr.People)
            .HasForeignKey(p => p.PracticeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Person>()
            .HasOne(p => p.LineManager)
            .WithMany()
            .HasForeignKey(p => p.LineManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Person>()
            .HasOne(p => p.HeadOfPractice)
            .WithMany()
            .HasForeignKey(p => p.HeadOfPracticeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Practice>()
            .HasOne(p => p.PracticeLead)
            .WithMany()
            .HasForeignKey(p => p.PracticeLeadId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict rather than cascade: removing a Person from a Project sets
        // ProjectMembership.RemovedAt (soft delete) instead of deleting the row, so
        // neither Project nor Person should ever cascade-delete membership history.
        modelBuilder.Entity<ProjectMembership>()
            .HasOne(m => m.Project)
            .WithMany(p => p.Memberships)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProjectMembership>()
            .HasOne(m => m.Person)
            .WithMany()
            .HasForeignKey(m => m.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
