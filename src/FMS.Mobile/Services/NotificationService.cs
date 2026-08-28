using Android.App;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;

namespace FMS.Mobile.Services;

/// <summary>
/// Android implementation of local notifications using NotificationManager.
/// </summary>
public class NotificationService : INotificationService
{
    private const string ChannelId = "fms_tasks";
    private const string ChannelName = "Farm Tasks";
    private const string ChannelDescription = "Notifications for overdue tasks and alerts";

    private readonly NotificationManager? _notificationManager;
    private readonly Activity? _activity;

    public NotificationService()
    {
        var context = Android.App.Application.Context;
        _notificationManager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        _activity = Platform.CurrentActivity;

        CreateNotificationChannel();
    }

    public async Task<bool> RequestPermissionAsync()
    {
        // Android 13+ requires POST_NOTIFICATIONS permission
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
            return status == PermissionStatus.Granted;
        }
        return true; // Below Android 13, no permission needed
    }

    public void ShowNotification(string title, string message, int notificationId = 0)
    {
        var context = Android.App.Application.Context;

        var builder = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo) // Uses built-in Android icon
            .SetContentTitle(title)
            .SetContentText(message)
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityDefault)
            .SetDefaults((int)(NotificationDefaults.Sound | NotificationDefaults.Vibrate));

        // Tap action: open the app
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
        if (intent != null)
        {
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var pendingIntent = PendingIntent.GetActivity(
                context,
                notificationId,
                intent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            builder.SetContentIntent(pendingIntent);
        }

        var notification = builder.Build();
        _notificationManager?.Notify(notificationId, notification);
    }

    public void CancelNotification(int notificationId)
    {
        _notificationManager?.Cancel(notificationId);
    }

    public void CancelAllNotifications()
    {
        _notificationManager?.CancelAll();
    }

    private void CreateNotificationChannel()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Default)
            {
                Description = ChannelDescription
            };
            _notificationManager?.CreateNotificationChannel(channel);
        }
    }
}
