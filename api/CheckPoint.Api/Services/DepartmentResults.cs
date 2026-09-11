using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum DepartmentCreationStatus { Created, ValidationFailed }

public record DepartmentCreationResult(DepartmentCreationStatus Status, DepartmentResponse? Department, string? Error)
{
    public static DepartmentCreationResult Created(DepartmentResponse department) =>
        new(DepartmentCreationStatus.Created, department, null);

    public static DepartmentCreationResult Invalid(string error) =>
        new(DepartmentCreationStatus.ValidationFailed, null, error);
}

public enum PracticeCreationStatus { Created, ValidationFailed, DepartmentNotFound }

public record PracticeCreationResult(PracticeCreationStatus Status, PracticeResponse? Practice, string? Error)
{
    public static PracticeCreationResult Created(PracticeResponse practice) =>
        new(PracticeCreationStatus.Created, practice, null);

    public static PracticeCreationResult Invalid(string error) =>
        new(PracticeCreationStatus.ValidationFailed, null, error);

    public static PracticeCreationResult DepartmentNotFound(string error) =>
        new(PracticeCreationStatus.DepartmentNotFound, null, error);
}

public enum PracticePeopleViewStatus { Success, PracticeNotFound, Forbidden }

public record PracticePeopleViewResult(
    PracticePeopleViewStatus Status, IReadOnlyList<PracticePersonResponse>? People, string? Error)
{
    public static PracticePeopleViewResult Success(IReadOnlyList<PracticePersonResponse> people) =>
        new(PracticePeopleViewStatus.Success, people, null);

    public static PracticePeopleViewResult PracticeNotFound(string error) =>
        new(PracticePeopleViewStatus.PracticeNotFound, null, error);

    public static PracticePeopleViewResult Forbidden() =>
        new(PracticePeopleViewStatus.Forbidden, null, null);
}
