namespace CheckPoint.Api.Domain;

// Singleton settings row backing the Admin Settings screen (spec Section 13,
// CBLT-251). Exactly one row ever exists — created lazily with these defaults
// the first time AdminSettingsService.GetAsync is called on a fresh database.
//
// Every field here already existed as a hardcoded default or an interim
// IOptions<T> config placeholder (formerly NewStarterCycleOptions, still
// GeneralCycleOptions, RequestDispatchOptions) — this table is the
// runtime-editable home those placeholders were always going to move to.
// CBLT-251 added the table and the Admin-only read/write surface;
// CBLT-252/253/254/255 each individually switch the consuming service from
// reading the old IOptions<T> to reading this row, one setting at a time —
// CBLT-252 (NewStarterIntervalWeeks, below) is the first to do so.
public class AppSettings
{
    public Guid Id { get; set; }

    // New Starter cycle check-in intervals, weeks after joining. Consumed by
    // ProjectService.AddPersonAsync (CBLT-252).
    public int[] NewStarterIntervalWeeks { get; set; } = [2, 4, 8];

    // CBLT-253 — skip the next FY quarter if it starts within this many weeks.
    public int GeneralCycleSkipThresholdWeeks { get; set; } = 4;

    // CBLT-254 — whether request emails send automatically on schedule, or
    // wait for a manual trigger.
    public bool AutomaticRequestSendingEnabled { get; set; } = true;

    // CBLT-255 — target POC counts per role, used by the completeness
    // indicator. Not a hard cap — a Project may have more or fewer.
    public int TargetTechPocCount { get; set; } = 1;
    public int TargetDmPocCount { get; set; } = 1;
    public int TargetOtherPocCount { get; set; } = 1;
}
