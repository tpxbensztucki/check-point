namespace CheckPoint.Api.Domain;

// The canned outcomes a catch-up can be resolved with (spec Section 5.3,
// CBLT-241). "Or free-text equivalent" from the ticket's own AC is satisfied
// by pairing Other with a required CatchUp.OutcomeNotes, rather than adding
// unlimited freeform categories here.
public enum CatchUpOutcomeType
{
    SixWeekCheckInAdded,
    NoActionClosed,
    EscalateFurther,
    Other,
}
