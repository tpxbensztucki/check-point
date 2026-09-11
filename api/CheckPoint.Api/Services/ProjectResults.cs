using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum ProjectCreationStatus { Created, ValidationFailed }

public record ProjectCreationResult(ProjectCreationStatus Status, ProjectResponse? Project, string? Error)
{
    public static ProjectCreationResult Created(ProjectResponse project) =>
        new(ProjectCreationStatus.Created, project, null);

    public static ProjectCreationResult Invalid(string error) =>
        new(ProjectCreationStatus.ValidationFailed, null, error);
}

public enum ProjectCompletionStatus { Completed, ValidationFailed, ProjectNotFound }

public record ProjectCompletionResult(ProjectCompletionStatus Status, ProjectResponse? Project, string? Error)
{
    public static ProjectCompletionResult Completed(ProjectResponse project) =>
        new(ProjectCompletionStatus.Completed, project, null);

    public static ProjectCompletionResult Invalid(string error) =>
        new(ProjectCompletionStatus.ValidationFailed, null, error);

    public static ProjectCompletionResult ProjectNotFound(string error) =>
        new(ProjectCompletionStatus.ProjectNotFound, null, error);
}

public enum ProjectMembershipStatus { Added, ValidationFailed, ProjectNotFound }

public record ProjectMembershipResult(ProjectMembershipStatus Status, ProjectMembershipResponse? Membership, string? Error)
{
    public static ProjectMembershipResult Added(ProjectMembershipResponse membership) =>
        new(ProjectMembershipStatus.Added, membership, null);

    public static ProjectMembershipResult Invalid(string error) =>
        new(ProjectMembershipStatus.ValidationFailed, null, error);

    public static ProjectMembershipResult ProjectNotFound(string error) =>
        new(ProjectMembershipStatus.ProjectNotFound, null, error);
}

public enum PersonProjectsStatus { Success, PersonNotFound, Forbidden }

public record PersonProjectsResult(PersonProjectsStatus Status, IReadOnlyList<PersonProjectSummary>? Projects, string? Error)
{
    public static PersonProjectsResult Success(IReadOnlyList<PersonProjectSummary> projects) =>
        new(PersonProjectsStatus.Success, projects, null);

    public static PersonProjectsResult PersonNotFound(string error) =>
        new(PersonProjectsStatus.PersonNotFound, null, error);

    public static PersonProjectsResult Forbidden() =>
        new(PersonProjectsStatus.Forbidden, null, null);
}

public enum ProjectMembershipRemovalStatus { Removed, ProjectNotFound, MembershipNotFound }

public record ProjectMembershipRemovalResult(ProjectMembershipRemovalStatus Status, string? Error)
{
    public static ProjectMembershipRemovalResult Removed() => new(ProjectMembershipRemovalStatus.Removed, null);

    public static ProjectMembershipRemovalResult ProjectNotFound(string error) =>
        new(ProjectMembershipRemovalStatus.ProjectNotFound, error);

    public static ProjectMembershipRemovalResult MembershipNotFound(string error) =>
        new(ProjectMembershipRemovalStatus.MembershipNotFound, error);
}
