namespace CheckPoint.Api.Contracts;

// Backs the Admin Settings screen (spec Section 13, CBLT-251). One flat
// response/request pair for the whole singleton settings row — the frontend
// groups fields into sections (Cycle Scheduling, Notifications, POC
// Requirements) for display, but the API has only one row to read/write.
public record AdminSettingsResponse(
    int[] NewStarterIntervalWeeks,
    int GeneralCycleSkipThresholdWeeks,
    bool AutomaticRequestSendingEnabled,
    int TargetTechPocCount,
    int TargetDmPocCount,
    int TargetOtherPocCount);

public record UpdateAdminSettingsRequest(
    int[] NewStarterIntervalWeeks,
    int GeneralCycleSkipThresholdWeeks,
    bool AutomaticRequestSendingEnabled,
    int TargetTechPocCount,
    int TargetDmPocCount,
    int TargetOtherPocCount);
