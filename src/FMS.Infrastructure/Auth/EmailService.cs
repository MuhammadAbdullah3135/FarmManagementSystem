using FMS.Application.Auth;
using Microsoft.Extensions.Logging;

namespace FMS.Infrastructure.Auth;

/// <summary>
/// PLACEHOLDER email service that logs the reset link instead of sending a real email.
/// Replace with real SMTP/SendGrid/AWS SES before production deployment.
/// </summary>
public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// PLACEHOLDER — logs the password reset link. Replace this implementation
    /// with a real email delivery provider (SMTP, SendGrid, AWS SES, etc.)
    /// before deploying to production.
    /// </summary>
    public Task SendPasswordResetEmailAsync(string email, string resetLink)
    {
        // PLACEHOLDER: In production, this would send an actual email via SMTP/SendGrid/AWS SES.
        // The reset link contains the raw token which is only present here and in the hashed DB record.
        _logger.LogInformation(
            "PLACEHOLDER EMAIL SERVICE: Password reset link for {Email}: {ResetLink}",
            email, resetLink);

        return Task.CompletedTask;
    }
}
