namespace CheckPoint.Api.Services;

// Abstracted so tests can substitute a recording fake instead of a real SMTP
// server (same reasoning as TimeProvider) — see RequestDispatchServiceTests.
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
