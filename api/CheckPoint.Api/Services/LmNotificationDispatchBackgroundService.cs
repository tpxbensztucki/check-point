namespace CheckPoint.Api.Services;

// Polls the LmNotification outbox and sends any not-yet-delivered rows (spec
// Section 6/7). Not registered in the "Testing" environment (see Program.cs) —
// same reasoning as RequestDispatchBackgroundService: it runs on real
// wall-clock time via Task.Delay rather than the injected TimeProvider, which
// would otherwise fire unpredictably against tests advancing a FakeTimeProvider.
public class LmNotificationDispatchBackgroundService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<LmNotificationDispatchService>();
                await service.DispatchPendingNotificationsAsync(stoppingToken);
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
