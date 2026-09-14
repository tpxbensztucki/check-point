namespace CheckPoint.Api.Services;

// No SMTP server exists in any environment this project has run in yet — these
// are the settings whoever stands one up (or points this at a transactional email
// provider's SMTP endpoint) will need to fill in via appsettings/environment
// variables. Host is deliberately empty so a misconfigured deployment fails loudly
// (SmtpEmailSender throws) rather than silently pretending to send.
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = "noreply@checkpoint.local";
    public string? Username { get; set; }
    public string? Password { get; set; }
}
