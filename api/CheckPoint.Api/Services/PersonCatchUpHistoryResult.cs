using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum PersonCatchUpHistoryStatus
{
    Success,
    PersonNotFound,
    Forbidden,
}

public record PersonCatchUpHistoryResult(
    PersonCatchUpHistoryStatus Status, PersonCatchUpHistoryResponse? History = null, string? Error = null)
{
    public static PersonCatchUpHistoryResult Success(PersonCatchUpHistoryResponse history) =>
        new(PersonCatchUpHistoryStatus.Success, history);

    public static readonly PersonCatchUpHistoryResult Forbidden = new(PersonCatchUpHistoryStatus.Forbidden);

    public static PersonCatchUpHistoryResult PersonNotFound(string error) =>
        new(PersonCatchUpHistoryStatus.PersonNotFound, Error: error);
}
