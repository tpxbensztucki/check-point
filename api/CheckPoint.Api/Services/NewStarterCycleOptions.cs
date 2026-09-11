namespace CheckPoint.Api.Services;

// Interim, until CBLT-252 (Admin Settings: Configure New Starter interval
// schedule) exists — that story will move this to a DB-backed, runtime-editable
// setting. Until then, binding from configuration (appsettings/environment
// variables) still satisfies "not hardcoded, changeable without a code change"
// (CBLT-226's acceptance criteria), it just isn't editable through the app itself
// yet.
public class NewStarterCycleOptions
{
    public const string SectionName = "NewStarterCycle";

    public int[] IntervalWeeks { get; set; } = [2, 4, 8];
}
