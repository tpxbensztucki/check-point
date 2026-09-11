namespace CheckPoint.Api.Domain;

// Leaver handling (marking a Person as a Leaver, retention rules, etc.) is a later
// story (Milestone 3) — this enum exists now so Person.Status has a real default.
public enum PersonStatus
{
    Employed,
    Leaver,
}
