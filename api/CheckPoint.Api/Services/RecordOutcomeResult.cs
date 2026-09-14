using CheckPoint.Api.Contracts;

namespace CheckPoint.Api.Services;

public enum RecordOutcomeStatus
{
    Recorded,
    CatchUpNotFound,
    Forbidden,
    AlreadyRecorded,
    Invalid,
}

public record RecordOutcomeResult(RecordOutcomeStatus Status, CatchUpResponse? CatchUp = null, string? Error = null)
{
    public static RecordOutcomeResult Recorded(CatchUpResponse catchUp) => new(RecordOutcomeStatus.Recorded, catchUp);

    public static readonly RecordOutcomeResult Forbidden = new(RecordOutcomeStatus.Forbidden);
    public static readonly RecordOutcomeResult AlreadyRecorded = new(
        RecordOutcomeStatus.AlreadyRecorded, Error: "This catch-up's outcome has already been recorded.");

    public static RecordOutcomeResult CatchUpNotFound(string error) => new(RecordOutcomeStatus.CatchUpNotFound, Error: error);

    public static RecordOutcomeResult Invalid(string error) => new(RecordOutcomeStatus.Invalid, Error: error);
}
