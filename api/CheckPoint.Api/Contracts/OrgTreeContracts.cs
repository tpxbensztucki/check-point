using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

// A forest, not a single tree — GetOrgTreeForViewerAsync returns one root node per
// Person who has no visible Line Manager in the caller's scope (either they have
// none at all, or their Line Manager falls outside what this viewer can see — see
// DepartmentService.GetPracticePeopleForViewerAsync for the same cross-practice
// rule). IsOrphaned uses the same definition as there: no LineManagerId, or the
// Line Manager's own PracticeId differs from this Person's.
public record OrgPersonNode(
    Guid Id,
    string FullName,
    PersonStatus Status,
    Guid PracticeId,
    IReadOnlyList<string> Roles,
    bool IsOrphaned,
    IReadOnlyList<OrgPersonNode> Reports);
