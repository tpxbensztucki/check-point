using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum PocStatusViewStatus
{
    Success,
    RequestNotFound,
    Forbidden,
}

public record PocStatusResult(PocStatusViewStatus Status, IReadOnlyList<PocResponseStatusEntry>? Entries = null, string? Error = null)
{
    public static PocStatusResult Success(IReadOnlyList<PocResponseStatusEntry> entries) =>
        new(PocStatusViewStatus.Success, entries);

    public static readonly PocStatusResult Forbidden = new(PocStatusViewStatus.Forbidden);

    public static PocStatusResult RequestNotFound(string error) => new(PocStatusViewStatus.RequestNotFound, Error: error);
}
