namespace CheckPoint.Api.Domain;

// Which stage of a cycle this request represents (spec Section 5.1/5.2) —
// distinct from FeedbackRequestStatus (whether it's fired yet). Needed so the
// cycle engine can recognise "the New Starter cycle's 4-week check-in" reliably
// rather than inferring it from ScheduledFor minus JoinedAt, which would break if
// intervals are reconfigured later (CBLT-252).
public enum FeedbackRequestStage
{
    NewStarterWeek2,
    NewStarterWeek4,

    // Auto-inserted only when the Week4 request is flagged — see
    // FeedbackCycleService.HandleCheckInFlaggedAsync (CBLT-227).
    NewStarterWeek6,
    NewStarterWeek8,

    // Recurring FY-quarter request once a membership has enrolled into the
    // General cycle (CBLT-228/229) — unlike the New Starter stages, quarters have
    // no distinct identity beyond "the next one," so a single value covers all of
    // them.
    General,
}
