namespace FMS.Mobile.Services;

/// <summary>
/// Local notification service for overdue tasks, alerts, etc.
/// Uses MAUI's built-in notification APIs (no push/FCM needed).
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Request notification permission from the user (Android 13+).
    /// </summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Show a local notification with the given title and message.
    /// </summary>
    void ShowNotification(string title, string message, int notificationId = 0);

    /// <summary>
    /// Cancel a specific notification by ID.
    /// </summary>
    void CancelNotification(int notificationId);

    /// <summary>
    /// Cancel all pending notifications.
    /// </summary>
    void CancelAllNotifications();
}
