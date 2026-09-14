using CheckPoint.Api.Services;

namespace CheckPoint.Api.IntegrationTests;

public record SentEmail(string To, string Subject, string Body);

// Records every send instead of talking to a real SMTP server, same reasoning as
// FakeTimeProvider standing in for TimeProvider.System.
public class RecordingEmailSender : IEmailSender
{
    public List<SentEmail> Sent { get; } = [];

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        Sent.Add(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }
}
