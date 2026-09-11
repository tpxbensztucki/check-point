using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum PocAssignmentStatus { Assigned, ValidationFailed, MembershipNotFound, Forbidden }

public record PocAssignmentResult(PocAssignmentStatus Status, ProjectMembershipPocsResponse? Pocs, string? Error)
{
    public static PocAssignmentResult Assigned(ProjectMembershipPocsResponse pocs) =>
        new(PocAssignmentStatus.Assigned, pocs, null);

    public static PocAssignmentResult Invalid(string error) =>
        new(PocAssignmentStatus.ValidationFailed, null, error);

    public static PocAssignmentResult MembershipNotFound(string error) =>
        new(PocAssignmentStatus.MembershipNotFound, null, error);

    public static PocAssignmentResult Forbidden() =>
        new(PocAssignmentStatus.Forbidden, null, null);
}

public enum PocMutationStatus { Success, ValidationFailed, MembershipNotFound, PocNotFound, Forbidden }

public record PocMutationResult(PocMutationStatus Status, ProjectMembershipPocsResponse? Pocs, string? Error)
{
    public static PocMutationResult Success(ProjectMembershipPocsResponse pocs) =>
        new(PocMutationStatus.Success, pocs, null);

    public static PocMutationResult Invalid(string error) =>
        new(PocMutationStatus.ValidationFailed, null, error);

    public static PocMutationResult MembershipNotFound(string error) =>
        new(PocMutationStatus.MembershipNotFound, null, error);

    public static PocMutationResult PocNotFound(string error) =>
        new(PocMutationStatus.PocNotFound, null, error);

    public static PocMutationResult Forbidden() =>
        new(PocMutationStatus.Forbidden, null, null);
}

public enum PocViewStatus { Success, MembershipNotFound, Forbidden }

public record PocViewResult(PocViewStatus Status, ProjectMembershipPocsResponse? Pocs, string? Error)
{
    public static PocViewResult Success(ProjectMembershipPocsResponse pocs) =>
        new(PocViewStatus.Success, pocs, null);

    public static PocViewResult MembershipNotFound(string error) =>
        new(PocViewStatus.MembershipNotFound, null, error);

    public static PocViewResult Forbidden() =>
        new(PocViewStatus.Forbidden, null, null);
}
