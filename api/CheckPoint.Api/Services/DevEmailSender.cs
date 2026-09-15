using Microsoft.Extensions.Logging;

namespace CheckPoint.Api.Services;

// Dev-only stand-in for SmtpEmailSender (CBLT-317) — logs the recipient,
// subject, and full body (which contains the magic-link/hotlink URL built in
// RequestDispatchService) instead of attempting a real send, since no SMTP
// server exists in any environment this project has run in yet (see
// SmtpOptions's own doc comment). This is what makes request dispatch and
// manual reminders actually completable in local dev — previously every send
// threw an SmtpException, confirmed directly while testing CBLT-236's
// reminder flow (CBLT-316's own repro case). Registered only outside
// Production (see Program.cs) — Production keeps SmtpEmailSender's
// fail-loudly-with-no-SMTP-configured behaviour unchanged.
public class DevEmailSender(ILogger<DevEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "=== DEV EMAIL (not actually sent — see DevEmailSender) ===\nTo: {To}\nSubject: {Subject}\n\n{Body}\n============================================================",
            to, subject, body);
        return Task.CompletedTask;
    }
}
