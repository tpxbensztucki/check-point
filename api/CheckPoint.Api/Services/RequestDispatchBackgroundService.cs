namespace CheckPoint.Api.Services;

// Polls for due Scheduled FeedbackRequests and dispatches them when the global
// mode is Automatic (spec Section 7). Not registered in the "Testing" environment
// (see Program.cs) — it runs on real wall-clock time via Task.Delay rather than
// the injected TimeProvider, which would otherwise fire unpredictably against
// integration tests that advance a FakeTimeProvider instead of real time.
public class RequestDispatchBackgroundService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<RequestDispatchService>();
                await service.DispatchDueAutomaticRequestsAsync(stoppingToken);
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
