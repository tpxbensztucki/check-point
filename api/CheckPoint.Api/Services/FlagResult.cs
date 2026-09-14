using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum FlagStatus
{
    Flagged,
    RequestNotFound,
    Forbidden,
}

public record FlagResult(FlagStatus Status, CatchUpResponse? CatchUp = null, string? Error = null)
{
    public static FlagResult Flagged(CatchUpResponse catchUp) => new(FlagStatus.Flagged, catchUp);

    public static readonly FlagResult Forbidden = new(FlagStatus.Forbidden);

    public static FlagResult RequestNotFound(string error) => new(FlagStatus.RequestNotFound, Error: error);
}
