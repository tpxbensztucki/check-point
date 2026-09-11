using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Departments, Practices, and Practice-scoped visibility (spec Sections 2/3).
public class DepartmentService(CheckPointDbContext db)
{
    public async Task<DepartmentCreationResult> CreateDepartmentAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return DepartmentCreationResult.Invalid("Name is required.");
        }

        var department = new Department { Name = name };
        db.Departments.Add(department);
        await db.SaveChangesAsync(cancellationToken);

        return DepartmentCreationResult.Created(new DepartmentResponse(department.Id, department.Name));
    }

    public async Task<PracticeCreationResult> CreatePracticeAsync(
        Guid departmentId, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return PracticeCreationResult.Invalid("Name is required.");
        }

        var departmentExists = await db.Departments.AnyAsync(d => d.Id == departmentId, cancellationToken);
        if (!departmentExists)
        {
            return PracticeCreationResult.DepartmentNotFound($"No Department found with id {departmentId}.");
        }

        var practice = new Practice { Name = name, DepartmentId = departmentId };
        db.Practices.Add(practice);
        await db.SaveChangesAsync(cancellationToken);

        return PracticeCreationResult.Created(new PracticeResponse(practice.Id, practice.Name, practice.DepartmentId));
    }

    // Visibility follows Practice tags, not reporting lines (spec Section 2): the
    // caller must be Admin or the Practice Lead of this specific Practice — not a
    // plain role check, so the caller's identity/roles are passed in rather than
    // resolved here.
    public async Task<PracticePeopleViewResult> GetPracticePeopleForViewerAsync(
        Guid practiceId,
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        CancellationToken cancellationToken = default)
    {
        var practice = await db.Practices.SingleOrDefaultAsync(p => p.Id == practiceId, cancellationToken);
        if (practice is null)
        {
            return PracticePeopleViewResult.PracticeNotFound($"No Practice found with id {practiceId}.");
        }

        var isLeadOfThisPractice = callerIsPracticeLead && practice.PracticeLeadId == callerId;
        if (!callerIsAdmin && !isLeadOfThisPractice)
        {
            return PracticePeopleViewResult.Forbidden();
        }

        // Filtering to this PracticeId before projecting is what keeps a Line
        // Manager tagged to a different Practice out of the results, even though
        // one of their reports (tagged here) is included and flagged Orphaned.
        var people = await db.People
            .Where(p => p.PracticeId == practiceId)
            .Select(p => new PracticePersonResponse(
                p.Id,
                p.FullName,
                p.Status,
                p.LineManagerId,
                p.HeadOfPracticeId,
                p.LineManagerId == null || p.LineManager!.PracticeId != p.PracticeId))
            .ToListAsync(cancellationToken);

        return PracticePeopleViewResult.Success(people);
    }
}
