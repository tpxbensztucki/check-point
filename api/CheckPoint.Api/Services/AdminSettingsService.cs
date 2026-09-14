using CheckPoint.Api.Contracts;
using CheckPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Admin Settings screen (spec Section 13, CBLT-251) — a single, lazily-created
// singleton row (see GetOrCreateRowAsync) covering every admin-configurable
// value that today lives in a hardcoded default or an interim IOptions<T>
// placeholder (NewStarterCycleOptions/GeneralCycleOptions/RequestDispatchOptions,
// each already flagged with a comment naming the ticket that will read from
// here instead). CBLT-251 only builds the row and this read/write surface;
// consuming services keep reading their old IOptions<T> until CBLT-252/253/
// 254/255 individually switch each one over.
public class AdminSettingsService(CheckPointDbContext db)
{
    public async Task<AdminSettingsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await GetOrCreateRowAsync(cancellationToken);
        return ToResponse(row);
    }

    public async Task<AdminSettingsUpdateResult> UpdateAsync(
        UpdateAdminSettingsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.NewStarterIntervalWeeks.Length == 0 || request.NewStarterIntervalWeeks.Any(w => w <= 0))
        {
            return AdminSettingsUpdateResult.Invalid(
                "New Starter intervals must be a non-empty list of positive week counts.");
        }

        if (request.GeneralCycleSkipThresholdWeeks <= 0)
        {
            return AdminSettingsUpdateResult.Invalid("The FY-quarter skip threshold must be a positive number of weeks.");
        }

        if (request.TargetTechPocCount < 0 || request.TargetDmPocCount < 0 || request.TargetOtherPocCount < 0)
        {
            return AdminSettingsUpdateResult.Invalid("Target POC counts cannot be negative.");
        }

        var row = await GetOrCreateRowAsync(cancellationToken);
        row.NewStarterIntervalWeeks = request.NewStarterIntervalWeeks;
        row.GeneralCycleSkipThresholdWeeks = request.GeneralCycleSkipThresholdWeeks;
        row.AutomaticRequestSendingEnabled = request.AutomaticRequestSendingEnabled;
        row.TargetTechPocCount = request.TargetTechPocCount;
        row.TargetDmPocCount = request.TargetDmPocCount;
        row.TargetOtherPocCount = request.TargetOtherPocCount;

        await db.SaveChangesAsync(cancellationToken);

        return AdminSettingsUpdateResult.Updated(ToResponse(row));
    }

    private async Task<AppSettings> GetOrCreateRowAsync(CancellationToken cancellationToken)
    {
        var row = await db.AppSettings.SingleOrDefaultAsync(cancellationToken);
        if (row is not null)
        {
            return row;
        }

        row = new AppSettings();
        db.AppSettings.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private static AdminSettingsResponse ToResponse(AppSettings row) => new(
        row.NewStarterIntervalWeeks,
        row.GeneralCycleSkipThresholdWeeks,
        row.AutomaticRequestSendingEnabled,
        row.TargetTechPocCount,
        row.TargetDmPocCount,
        row.TargetOtherPocCount);
}
