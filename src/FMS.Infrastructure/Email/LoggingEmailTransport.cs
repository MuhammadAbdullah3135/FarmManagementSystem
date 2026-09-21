using FMS.Application.Email;
using Microsoft.Extensions.Logging;

namespace FMS.Infrastructure.Email;

/// <summary>
/// The default transport: logs the rendered message instead of sending it.
///
/// This is the behaviour the application shipped with, made explicit and selectable
/// rather than left as an oversight. It keeps three things working with no
/// credentials anywhere: a fresh clone, the test host, and Development - where the
/// logged body is how you complete a password reset or an invitation by hand.
///
/// It is not delivery, so it must not be selected in production: the log line is the
/// only place the message goes. EmailConfigurationGuard says so at startup, and the
/// body contains single-use links, which is another reason this is a Development and
/// testing transport rather than a production one.
/// </summary>
public class LoggingEmailTransport : IEmailTransport
{
    private readonly ILogger<LoggingEmailTransport> _logger;

    public LoggingEmailTransport(ILogger<LoggingEmailTransport> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Email delivery is disabled (Email:Provider = Log), so nothing was sent. "
            + "To {Recipient} | Subject: {Subject} | Body: {Body}",
            message.To,
            message.Subject,
            message.TextBody);

        return Task.CompletedTask;
    }
}
