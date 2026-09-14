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
    public DbSet<CatchUp> CatchUps => Set<CatchUp>();
    public DbSet<FeedbackSubmission> FeedbackSubmissions => Set<FeedbackSubmission>();
    public DbSet<LmNotification> LmNotifications => Set<LmNotification>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

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

        // Restrict rather than cascade: a Poc being removed must never silently
        // invalidate/delete an already-issued link's history.
        modelBuilder.Entity<MagicLink>()
            .HasOne(l => l.Poc)
            .WithMany()
            .HasForeignKey(l => l.PocId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // Restrict on both sides — history must survive either way. FeedbackRequestId
        // is nullable (CBLT-240): an ad-hoc review's CatchUp has no check-in
        // reference at all, unlike one created by flagging a specific check-in.
        modelBuilder.Entity<CatchUp>()
            .HasOne(c => c.Person)
            .WithMany()
            .HasForeignKey(c => c.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CatchUp>()
            .HasOne(c => c.FeedbackRequest)
            .WithMany()
            .HasForeignKey(c => c.FeedbackRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        // A POC can submit at most once per request (not once per request overall
        // — each currently-assigned POC gives independent feedback, CBLT-302).
        // Enforced here, not just by service logic, since the magic link's own
        // already-used guard is the primary defence and this is a second,
        // structural backstop.
        modelBuilder.Entity<FeedbackSubmission>()
            .HasIndex(s => new { s.FeedbackRequestId, s.PocId })
            .IsUnique();

        modelBuilder.Entity<FeedbackSubmission>()
            .HasOne(s => s.FeedbackRequest)
            .WithMany()
            .HasForeignKey(s => s.FeedbackRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FeedbackSubmission>()
            .HasOne(s => s.Poc)
            .WithMany()
            .HasForeignKey(s => s.PocId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict on the LineManager side (an outbox row must never vanish along
        // with the manager row); default cascade on FeedbackSubmission, since a
        // notification is meaningless without the submission that triggered it.
        modelBuilder.Entity<LmNotification>()
            .HasOne(n => n.LineManager)
            .WithMany()
            .HasForeignKey(n => n.LineManagerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict on both sides — an audit entry must never be lost, even in
        // a hypothetical future where a Person row is removed (Person is
        // never actually deleted today, only marked Leaver, but this matches
        // every other Person-referencing entity's convention in this file).
        modelBuilder.Entity<AuditLogEntry>()
            .HasOne(e => e.Viewer)
            .WithMany()
            .HasForeignKey(e => e.ViewerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLogEntry>()
            .HasOne(e => e.Person)
            .WithMany()
            .HasForeignKey(e => e.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
