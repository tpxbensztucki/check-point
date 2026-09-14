namespace CheckPoint.Api.Services;

public enum ReminderStatus
{
    Sent,
    RequestNotFound,
    PocNotFound,
    Forbidden,
    NotYetDispatched,
    AlreadySubmitted,
}

public record ReminderResult(ReminderStatus Status, string? Error = null)
{
    public static readonly ReminderResult Sent = new(ReminderStatus.Sent);
    public static readonly ReminderResult Forbidden = new(ReminderStatus.Forbidden);

    public static ReminderResult RequestNotFound(string error) => new(ReminderStatus.RequestNotFound, error);
    public static ReminderResult PocNotFound(string error) => new(ReminderStatus.PocNotFound, error);
    public static ReminderResult NotYetDispatched(string error) => new(ReminderStatus.NotYetDispatched, error);
    public static ReminderResult AlreadySubmitted(string error) => new(ReminderStatus.AlreadySubmitted, error);
}
