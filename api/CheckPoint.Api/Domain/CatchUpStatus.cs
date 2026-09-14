namespace CheckPoint.Api.Domain;

public enum CatchUpStatus
{
    Pending,

    // Set once its outcome is recorded (CBLT-241) — see CatchUp.OutcomeType/
    // OutcomeNotes/RecordedAt, all set together with this transition.
    Recorded,
}
