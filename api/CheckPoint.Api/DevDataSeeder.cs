using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api;

// Dev/local-only convenience: one Person per role (plus a plain report with
// no roles) so the dev sign-in picker (CBLT-304, GET /dev/people) has
// something real to switch between when debugging role-scoped behaviour —
// there's otherwise no way to get a first Person into an empty database
// without already being an authenticated Admin. Only ever runs outside
// Production (same gate as DevPersonAuthenticationHandler/DevEndpoints) and
// only when the People table is completely empty, so it never runs against
// a database that already has real or previously-seeded data.
public static class DevDataSeeder
{
    public static async Task SeedAsync(CheckPointDbContext db)
    {
        if (await db.People.AnyAsync())
        {
            return;
        }

        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var practiceLeadRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        var department = new Department { Name = "Tech & Data" };
        var practice = new Practice { Name = "Software Engineering", Department = department };
        db.Departments.Add(department);
        db.Practices.Add(practice);
        await db.SaveChangesAsync();

        var admin = new Person
        {
            FullName = "Ada Admin",
            Email = "ada.admin@example.com",
            PracticeId = practice.Id,
            Roles = [adminRole],
        };
        var practiceLead = new Person
        {
            FullName = "Lee Lead",
            Email = "lee.lead@example.com",
            PracticeId = practice.Id,
            Roles = [practiceLeadRole],
        };
        var lineManager = new Person
        {
            FullName = "Morgan Manager",
            Email = "morgan.manager@example.com",
            PracticeId = practice.Id,
            Roles = [lineManagerRole],
        };
        db.People.AddRange(admin, practiceLead, lineManager);
        await db.SaveChangesAsync();

        practice.PracticeLeadId = practiceLead.Id;

        var report = new Person
        {
            FullName = "Riley Report",
            Email = "riley.report@example.com",
            PracticeId = practice.Id,
            LineManagerId = lineManager.Id,
        };
        db.People.Add(report);
        await db.SaveChangesAsync();
    }
}
