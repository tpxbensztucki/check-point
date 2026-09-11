using CheckPoint.Api.Domain;

namespace CheckPoint.Api.Contracts;

public record CreateDepartmentRequest(string Name);
public record CreatePracticeRequest(string Name);
public record DepartmentResponse(Guid Id, string Name);
public record PracticeResponse(Guid Id, string Name, Guid DepartmentId);

// IsOrphaned is true when LineManagerId is unset, or the Line Manager's own
// PracticeId differs from this Person's (spec Section 2) — computed on every read
// by DepartmentService, not stored, so it can never go stale when either Person's
// Practice or Line Manager changes.
public record PracticePersonResponse(
    Guid Id,
    string FullName,
    PersonStatus Status,
    Guid? LineManagerId,
    Guid? HeadOfPracticeId,
    bool IsOrphaned);
