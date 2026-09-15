using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum PersonCreationStatus { Created, ValidationFailed }

public record PersonCreationResult(PersonCreationStatus Status, PersonResponse? Person, string? Error)
{
    public static PersonCreationResult Created(PersonResponse person) =>
        new(PersonCreationStatus.Created, person, null);

    public static PersonCreationResult Invalid(string error) =>
        new(PersonCreationStatus.ValidationFailed, null, error);
}

public enum PersonUpdateStatus { Updated, ValidationFailed, NotFound }

public record PersonUpdateResult(PersonUpdateStatus Status, PersonResponse? Person, string? Error)
{
    public static PersonUpdateResult Updated(PersonResponse person) =>
        new(PersonUpdateStatus.Updated, person, null);

    public static PersonUpdateResult Invalid(string error) =>
        new(PersonUpdateStatus.ValidationFailed, null, error);

    public static PersonUpdateResult NotFound(string error) =>
        new(PersonUpdateStatus.NotFound, null, error);
}

public enum RoleAssignmentStatus { Assigned, ValidationFailed, PersonNotFound }

public record RoleAssignmentResult(RoleAssignmentStatus Status, PersonRolesResponse? Roles, string? Error)
{
    public static RoleAssignmentResult Assigned(PersonRolesResponse roles) =>
        new(RoleAssignmentStatus.Assigned, roles, null);

    public static RoleAssignmentResult Invalid(string error) =>
        new(RoleAssignmentStatus.ValidationFailed, null, error);

    public static RoleAssignmentResult PersonNotFound(string error) =>
        new(RoleAssignmentStatus.PersonNotFound, null, error);
}

public enum RoleRemovalStatus { Removed, ValidationFailed, PersonNotFound }

public record RoleRemovalResult(RoleRemovalStatus Status, PersonRolesResponse? Roles, string? Error)
{
    public static RoleRemovalResult Removed(PersonRolesResponse roles) =>
        new(RoleRemovalStatus.Removed, roles, null);

    public static RoleRemovalResult Invalid(string error) =>
        new(RoleRemovalStatus.ValidationFailed, null, error);

    public static RoleRemovalResult PersonNotFound(string error) =>
        new(RoleRemovalStatus.PersonNotFound, null, error);
}

// Backs the scoped single-Person profile read (CBLT-240 frontend follow-up) —
// the three-way (Admin/Practice Lead/Line Manager) equivalent of GetAllAsync's
// Admin-only flat list, but for exactly one Person. Reuses PersonListEntry
// since it already carries everything a profile view needs.
public enum PersonProfileStatus { Success, PersonNotFound, Forbidden }

public record PersonProfileResult(PersonProfileStatus Status, PersonListEntry? Person, string? Error)
{
    public static PersonProfileResult Success(PersonListEntry person) =>
        new(PersonProfileStatus.Success, person, null);

    public static PersonProfileResult PersonNotFound(string error) =>
        new(PersonProfileStatus.PersonNotFound, null, error);

    public static PersonProfileResult Forbidden() =>
        new(PersonProfileStatus.Forbidden, null, null);
}

public enum LeaverTransitionStatus { MarkedAsLeaver, ValidationFailed, PersonNotFound, Forbidden }

public record LeaverTransitionResult(LeaverTransitionStatus Status, PersonResponse? Person, string? Error)
{
    public static LeaverTransitionResult MarkedAsLeaver(PersonResponse person) =>
        new(LeaverTransitionStatus.MarkedAsLeaver, person, null);

    public static LeaverTransitionResult Invalid(string error) =>
        new(LeaverTransitionStatus.ValidationFailed, null, error);

    public static LeaverTransitionResult PersonNotFound(string error) =>
        new(LeaverTransitionStatus.PersonNotFound, null, error);

    public static LeaverTransitionResult Forbidden() =>
        new(LeaverTransitionStatus.Forbidden, null, null);
}
