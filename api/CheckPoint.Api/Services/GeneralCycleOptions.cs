namespace CheckPoint.Api.Services;

// Interim, until CBLT-253 (Admin Settings: Configure FY-quarter skip threshold)
// exists — same stand-in pattern as NewStarterCycleOptions (CBLT-226).
public class GeneralCycleOptions
{
    public const string SectionName = "GeneralCycle";

    public int SkipThresholdWeeks { get; set; } = 4;
}
