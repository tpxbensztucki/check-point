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
