using FMS.Application.Auth;
using FMS.Application.Notifications;
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

    /// <summary>
    /// PLACEHOLDER — logs the farm invitation link. Replace this implementation
    /// with a real email delivery provider (SMTP, SendGrid, AWS SES, etc.)
    /// before deploying to production.
    /// </summary>
    public Task SendFarmInvitationEmailAsync(string email, string farmName, string inviteLink)
    {
        // PLACEHOLDER: In production, this would send an actual email. The link
        // carries the raw token, which is only present here and in the hashed DB record.
        _logger.LogInformation(
            "PLACEHOLDER EMAIL SERVICE: Farm invitation for {Email} to join {FarmName}: {InviteLink}",
            email, farmName, inviteLink);

        return Task.CompletedTask;
    }

    /// <summary>
    /// PLACEHOLDER — logs the alert digest. Replace this implementation with a real
    /// email delivery provider (SMTP, SendGrid, AWS SES, etc.) before deploying to
    /// production.
    ///
    /// The digest is one message per recipient per dispatch run rather than one per
    /// alert, so it stays readable on a farm with many simultaneous alerts. The
    /// alert text is identical to the notification center's, so the two channels
    /// cannot disagree about what happened.
    /// </summary>
    public Task SendAlertNotificationsEmailAsync(
        string email,
        string farmName,
        IReadOnlyList<NotificationEmailItem> notifications)
    {
        // PLACEHOLDER: in production this would be handed to the provider.
        _logger.LogInformation(
            "PLACEHOLDER EMAIL SERVICE: {AlertCount} farm alert(s) for {Email} on {FarmName}: {Alerts}",
            notifications.Count,
            email,
            farmName,
            string.Join(
                " | ",
                notifications.Select(n => $"[{n.Severity}] {n.Title} — {n.Message}")));

        return Task.CompletedTask;
    }
}
