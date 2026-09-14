namespace CheckPoint.Api.Domain;

// Singleton settings row backing the Admin Settings screen (spec Section 13,
// CBLT-251). Exactly one row ever exists — created lazily with these defaults
// the first time AdminSettingsService.GetAsync is called on a fresh database.
//
// Every field here used to live in a hardcoded default or an interim
// IOptions<T> config placeholder (NewStarterCycleOptions, GeneralCycleOptions,
// RequestDispatchOptions, all now deleted — CBLT-252/253/254) — this table is
// the runtime-editable home those placeholders were always going to move to.
// CBLT-251 added the table and the Admin-only read/write surface;
// CBLT-252/253/254/255 each individually switched the consuming service from
// reading the old IOptions<T> to reading this row, one setting at a time.
public class AppSettings
{
    public Guid Id { get; set; }

    // New Starter cycle check-in intervals, weeks after joining. Consumed by
    // ProjectService.AddPersonAsync (CBLT-252).
    public int[] NewStarterIntervalWeeks { get; set; } = [2, 4, 8];

    // Skip the next FY quarter if it starts within this many weeks. Consumed
    // by FeedbackCycleService.EnrolIntoGeneralCycleAsync (CBLT-253).
    public int GeneralCycleSkipThresholdWeeks { get; set; } = 4;

    // Whether request emails send automatically on schedule, or wait for a
    // manual trigger. Consumed by
    // RequestDispatchService.DispatchDueAutomaticRequestsAsync (CBLT-254).
    public bool AutomaticRequestSendingEnabled { get; set; } = true;

    // CBLT-255 — target POC counts per role, used by the completeness
    // indicator. Not a hard cap — a Project may have more or fewer.
    public int TargetTechPocCount { get; set; } = 1;
    public int TargetDmPocCount { get; set; } = 1;
    public int TargetOtherPocCount { get; set; } = 1;
}
