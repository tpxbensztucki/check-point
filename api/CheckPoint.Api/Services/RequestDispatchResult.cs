namespace CheckPoint.Api.Services;

public enum RequestDispatchStatus
{
    Dispatched,
    RequestNotFound,
    Forbidden,
    NotCurrentlyScheduled,
    NoPocsAssigned,
}

public record RequestDispatchResult(RequestDispatchStatus Status, string? Error = null)
{
    public static readonly RequestDispatchResult Dispatched = new(RequestDispatchStatus.Dispatched);
    public static readonly RequestDispatchResult Forbidden = new(RequestDispatchStatus.Forbidden);

    public static RequestDispatchResult RequestNotFound(string error) =>
        new(RequestDispatchStatus.RequestNotFound, error);

    public static RequestDispatchResult NotCurrentlyScheduled(string error) =>
        new(RequestDispatchStatus.NotCurrentlyScheduled, error);

    public static RequestDispatchResult NoPocsAssigned(string error) =>
        new(RequestDispatchStatus.NoPocsAssigned, error);
}
