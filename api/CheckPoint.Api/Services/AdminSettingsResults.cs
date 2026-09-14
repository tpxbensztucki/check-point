using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum AdminSettingsUpdateStatus
{
    Updated,
    Invalid,
}

public class AdminSettingsUpdateResult
{
    public required AdminSettingsUpdateStatus Status { get; init; }
    public AdminSettingsResponse? Settings { get; init; }
    public string? Error { get; init; }

    public static AdminSettingsUpdateResult Updated(AdminSettingsResponse settings) =>
        new() { Status = AdminSettingsUpdateStatus.Updated, Settings = settings };

    public static AdminSettingsUpdateResult Invalid(string error) =>
        new() { Status = AdminSettingsUpdateStatus.Invalid, Error = error };
}
