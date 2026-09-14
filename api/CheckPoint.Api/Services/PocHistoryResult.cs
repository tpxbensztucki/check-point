using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum PocHistoryStatus
{
    Success,
    PocNotFound,
    Forbidden,
}

public record PocHistoryResult(PocHistoryStatus Status, PocResponseHistoryResponse? History = null, string? Error = null)
{
    public static PocHistoryResult Success(PocResponseHistoryResponse history) => new(PocHistoryStatus.Success, history);

    public static readonly PocHistoryResult Forbidden = new(PocHistoryStatus.Forbidden);

    public static PocHistoryResult PocNotFound(string error) => new(PocHistoryStatus.PocNotFound, Error: error);
}

public enum ProjectPocPatternsStatus
{
    Success,
    ProjectNotFound,
}

public record ProjectPocPatternsResult(
    ProjectPocPatternsStatus Status, IReadOnlyList<ProjectPocPatternEntry>? Entries = null, string? Error = null)
{
    public static ProjectPocPatternsResult Success(IReadOnlyList<ProjectPocPatternEntry> entries) =>
        new(ProjectPocPatternsStatus.Success, entries);

    public static ProjectPocPatternsResult ProjectNotFound(string error) =>
        new(ProjectPocPatternsStatus.ProjectNotFound, Error: error);
}
