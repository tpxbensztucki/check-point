using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// The hierarchical org tree, scoped to what the viewer's roles let them see (spec
// Section 2). No failure case exists here worth a Status/Result type (see
// PersonResults.cs/DepartmentResults.cs for that pattern) — every authenticated
// caller gets back a tree, possibly an empty one if they hold none of the three
// roles that grant any visibility.
public class OrgTreeService(CheckPointDbContext db)
{
    private record PersonNode(
        Guid Id,
        string FullName,
        PersonStatus Status,
        Guid PracticeId,
        Guid? LineManagerId,
        IReadOnlyList<string> Roles,
        bool IsOrphaned);

    public async Task<IReadOnlyList<OrgPersonNode>> GetOrgTreeForViewerAsync(
        Guid callerId,
        bool callerIsAdmin,
        bool callerIsPracticeLead,
        bool callerIsLineManager,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Person> scope = db.People;

        var visibleIds = await PersonAuthorizationHelpers.GetVisiblePersonIdsAsync(
            db, callerId, callerIsAdmin, callerIsPracticeLead, callerIsLineManager, cancellationToken);

        if (visibleIds is not null)
        {
            if (visibleIds.Count == 0)
            {
                return [];
            }

            scope = scope.Where(p => visibleIds.Contains(p.Id));
        }

        var people = await scope
            .Select(p => new PersonNode(
                p.Id,
                p.FullName,
                p.Status,
                p.PracticeId,
                p.LineManagerId,
                p.Roles.Select(r => r.Name).ToList(),
                p.LineManagerId == null || p.LineManager!.PracticeId != p.PracticeId))
            .ToListAsync(cancellationToken);

        return BuildForest(people);
    }

    // A Person's Line Manager may fall outside this viewer's scope (a different
    // Practice Lead's report, or simply not among a Line Manager's direct
    // reports) — such a Person becomes a root here rather than being dropped,
    // exactly like the Practice-view rule that excludes an out-of-scope manager
    // while still showing their report.
    private static List<OrgPersonNode> BuildForest(List<PersonNode> people)
    {
        var visibleIds = people.Select(p => p.Id).ToHashSet();
        var childrenByManagerId = people
            .Where(p => p.LineManagerId is { } managerId && visibleIds.Contains(managerId))
            .GroupBy(p => p.LineManagerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var roots = people.Where(p => p.LineManagerId is null || !visibleIds.Contains(p.LineManagerId.Value));

        return roots
            .Select(root =>
            {
                var ancestry = new HashSet<Guid> { root.Id };
                return ToNode(root, childrenByManagerId, ancestry);
            })
            .OrderBy(n => n.FullName)
            .ToList();
    }

    private static OrgPersonNode ToNode(
        PersonNode person, Dictionary<Guid, List<PersonNode>> childrenByManagerId, HashSet<Guid> ancestry)
    {
        var reports = new List<OrgPersonNode>();
        if (childrenByManagerId.TryGetValue(person.Id, out var kids))
        {
            foreach (var kid in kids)
            {
                // Guards against a manager cycle in the data (e.g. two separate
                // edits leaving A -> B -> A) turning this into infinite recursion.
                if (!ancestry.Add(kid.Id))
                {
                    continue;
                }

                reports.Add(ToNode(kid, childrenByManagerId, ancestry));
                ancestry.Remove(kid.Id);
            }
        }

        return new OrgPersonNode(
            person.Id,
            person.FullName,
            person.Status,
            person.PracticeId,
            person.Roles,
            person.IsOrphaned,
            reports.OrderBy(n => n.FullName).ToList());
    }
}
