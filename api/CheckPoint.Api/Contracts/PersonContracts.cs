using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record CreatePersonRequest(
    string FullName,
    Guid PracticeId,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId,
    string? Email = null);

public record UpdatePersonRequest(
    string FullName,
    Guid PracticeId,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId,
    string? Email = null);

public record PersonResponse(
    Guid Id,
    string FullName,
    PersonStatus Status,
    Guid PracticeId,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId,
    string? Email);

// PracticeId is required when RoleName is RoleNames.PracticeLead (designates which
// Practice the Person owns as its lead) and ignored otherwise.
public record AssignRoleRequest(string RoleName, Guid? PracticeId);

public record PersonRolesResponse(Guid PersonId, IReadOnlyList<string> Roles);

// Backs the Admin Console's People list (CBLT-306) — a flat browse view,
// distinct from PersonResponse (a single create/update result) in that it
// resolves display names for Practice/LineManager and includes Roles, so
// the list doesn't need a second round trip per row.
public record PersonListEntry(
    Guid Id,
    string FullName,
    PersonStatus Status,
    Guid PracticeId,
    string PracticeName,
    Guid? LineManagerId,
    string? LineManagerName,
    Guid? HeadOfPracticeId,
    IReadOnlyList<string> Roles,
    string? Email);
