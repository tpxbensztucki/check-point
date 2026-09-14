namespace CheckPoint.Api.Services;

// Polls for Leavers past the 6-month retention window and purges their
// feedback data (spec Section 11, CBLT-248). Same not-registered-in-
// "Testing" pattern as RequestDispatchBackgroundService/
// LmNotificationDispatchBackgroundService — real wall-clock Task.Delay would
// otherwise fire unpredictably against a FakeTimeProvider-driven test. A
// daily interval is a deliberate choice for a 6-month-resolution job — there
// is no need for the 1-minute cadence the per-request dispatch jobs use.
public class LeaverRetentionBackgroundService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<LeaverRetentionService>();
                await service.PurgeExpiredLeaversAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }
}
