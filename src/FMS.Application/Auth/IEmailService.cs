using FMS.Application.Notifications;

namespace FMS.Application.Auth;

/// <summary>One alert inside a notification digest email.</summary>
public readonly record struct NotificationEmailItem(
    string Severity,
    string Title,
    string Message,
    string? Link);

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string email, string resetLink);

    /// <summary>
    /// Invites <paramref name="email"/> to join <paramref name="farmName"/> using
    /// the single-use link in <paramref name="inviteLink"/>.
    /// </summary>
    Task SendFarmInvitationEmailAsync(string email, string farmName, string inviteLink);

    /// <summary>
    /// Sends one alert digest for <paramref name="farmName"/>.
    ///
    /// Deliberately batched rather than one email per alert: a farm can raise
    /// dozens of alerts in a single dispatch run, and a mailbox full of separate
    /// messages is the fastest way to get a useful channel filtered as spam.
    /// </summary>
    Task SendAlertNotificationsEmailAsync(
        string email,
        string farmName,
        IReadOnlyList<NotificationEmailItem> notifications);
}
