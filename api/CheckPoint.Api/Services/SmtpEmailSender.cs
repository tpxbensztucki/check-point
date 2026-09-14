using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace CheckPoint.Api.Services;

// The real (production) IEmailSender. Uses the BCL's SmtpClient rather than
// adding a NuGet dependency on a specific transactional-email provider, since no
// provider has been chosen for this project yet — whoever configures SmtpOptions
// can point this at any standard SMTP endpoint (including most providers'
// SMTP-compatible relay).
public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.Host, settings.Port) { EnableSsl = settings.EnableSsl };
        if (!string.IsNullOrEmpty(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        using var message = new MailMessage(settings.FromAddress, to, subject, body);
        await client.SendMailAsync(message, cancellationToken);
    }
}
