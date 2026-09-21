using FMS.Application.Common;

namespace FMS.Application.Notifications;

/// <summary>
/// Reads and updates notifications for <em>the current user</em>.
///
/// Every method takes the recipient explicitly and the implementation filters on
/// it, so a notification can never be read or acknowledged across users: the
/// recipient is never taken from the request.
/// </summary>
public interface INotificationService
{
    Task<Result<NotificationListDto>> GetAsync(Guid farmId, Guid userId, NotificationQuery query);

    Task<Result<int>> GetUnreadCountAsync(Guid farmId, Guid userId);

    /// <summary>Marks one of the caller's own notifications read. Missing or somebody else's → NotFound.</summary>
    Task<Result<NotificationDto>> MarkReadAsync(Guid farmId, Guid userId, Guid notificationId);

    /// <summary>Marks every unread notification for this recipient read. Returns how many changed.</summary>
    Task<Result<int>> MarkAllReadAsync(Guid farmId, Guid userId);

    /// <summary>
    /// Dismisses one of the caller's own notifications: it leaves their list but
    /// the row survives, so the record is not rewritten.
    /// </summary>
    Task<Result<NotificationDto>> DismissAsync(Guid farmId, Guid userId, Guid notificationId);

    Task<Result<NotificationPreferenceSettingsDto>> GetPreferencesAsync(Guid farmId, Guid userId);

    Task<Result<NotificationPreferenceSettingsDto>> UpdatePreferencesAsync(
        Guid farmId,
        Guid userId,
        UpdateNotificationPreferencesRequest request);
}
