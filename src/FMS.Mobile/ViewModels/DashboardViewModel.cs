using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;
    private readonly IDatabaseService _db;
    private readonly IReferenceDataService _refData;
    private readonly INotificationService _notifications;

    [ObservableProperty] private int _totalAnimals;
    [ObservableProperty] private int _pregnantCount;
    [ObservableProperty] private int _sickCount;
    [ObservableProperty] private int _dueVaccinationCount;
    [ObservableProperty] private int _overdueTasks;
    [ObservableProperty] private int _upcomingBirths;

    /// <summary>Detailed error messages from the last sync — shown in an expandable section.</summary>
    public ObservableCollection<SyncErrorDetail> SyncErrors { get; } = new();

    [ObservableProperty] private bool _showSyncErrors;

    private bool _notificationsFired;

    public DashboardViewModel(IApiService api, IAuthService auth, ISyncService sync, IDatabaseService db,
        IReferenceDataService refData, IConnectivityService connectivity, INotificationService notifications)
    {
        _api = api;
        _auth = auth;
        _sync = sync;
        _db = db;
        _refData = refData;
        _notifications = notifications;
        Title = "Dashboard";
        InitializeSyncServices(connectivity, db, auth, sync);
    }

    [RelayCommand]
    private async Task LoadDashboardAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        _api.SetFarmHeader(farmId.Value);
        await RefreshSyncStatusAsync();

        await RunBusyAsync(async () =>
        {
            try
            {
                var summary = await _api.GetAsync<DashboardSummary>($"/farm/{farmId}/dashboard/summary");
                if (summary is not null)
                {
                    TotalAnimals = summary.TotalAnimals;
                    PregnantCount = summary.PregnantCount;
                    SickCount = summary.SickCount;
                    DueVaccinationCount = summary.DueVaccinationCount;
                    OverdueTasks = summary.OverdueTasks;
                    UpcomingBirths = summary.UpcomingBirths;
                }

                Alerts.Clear();
                var alerts = await _api.GetAsync<List<AlertItem>>($"/farm/{farmId}/dashboard/alerts");
                if (alerts is not null)
                {
                    foreach (var alert in alerts.Take(10))
                        Alerts.Add(alert);
                }

                // Fire notifications on first dashboard load
                if (!_notificationsFired)
                {
                    _notificationsFired = true;
                    await FireAlertNotificationsAsync();
                }
            }
            catch { /* Dashboard loads best-effort */ }
        });
    }

    public ObservableCollection<AlertItem> Alerts { get; } = new();

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        IsSyncing = true;
        SyncProgressText = "Syncing...";
        SyncStatusText = null;
        SyncErrors.Clear();
        ShowSyncErrors = false;

        var progress = new Progress<SyncProgress>(p =>
        {
            SyncProgressText = $"Syncing {p.Current}/{p.Total}: {p.CurrentEntity}";
        });

        try
        {
            var result = await _sync.SyncPendingRecordsAsync(farmId.Value, progress);
            await _sync.SyncFarmAnimalsAsync(farmId.Value);
            await _refData.SyncAllReferenceDataAsync(farmId.Value);

            LastSyncTime = _sync.LastSyncTime;
            PendingSyncCount = await _db.GetPendingCountAsync(farmId.Value);
            FailedCount = result.Failed;

            // Surface individual errors
            if (result.Errors.Count > 0)
            {
                foreach (var error in result.Errors)
                    SyncErrors.Add(new SyncErrorDetail
                    {
                        EntityType = error.EntityType,
                        ErrorMessage = error.ErrorMessage
                    });
                ShowSyncErrors = true;
            }

            SyncStatusText = result.Failed > 0
                ? $"Synced {result.Synced}, {result.Failed} failed"
                : $"All {result.Synced} records synced";
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
            SyncProgressText = null;
        }

        await LoadDashboardAsync();
    }

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        IsSyncing = true;
        SyncProgressText = "Retrying failed records...";
        SyncErrors.Clear();
        ShowSyncErrors = false;

        try
        {
            var result = await _sync.RetryFailedRecordsAsync(farmId.Value);
            LastSyncTime = _sync.LastSyncTime;
            PendingSyncCount = await _db.GetPendingCountAsync(farmId.Value);
            FailedCount = result.Failed;

            if (result.Errors.Count > 0)
            {
                foreach (var error in result.Errors)
                    SyncErrors.Add(new SyncErrorDetail
                    {
                        EntityType = error.EntityType,
                        ErrorMessage = error.ErrorMessage
                    });
                ShowSyncErrors = true;
            }

            SyncStatusText = result.Failed > 0
                ? $"Retried: {result.Synced} synced, {result.Failed} still failed"
                : $"All {result.Synced} records synced";
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Retry failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
            SyncProgressText = null;
        }

        await LoadDashboardAsync();
    }

    private async Task FireAlertNotificationsAsync()
    {
        var hasPermission = await _notifications.RequestPermissionAsync();
        if (!hasPermission) return;

        var notifId = 1000;

        if (OverdueTasks > 0)
        {
            _notifications.ShowNotification(
                $"{OverdueTasks} Overdue Task{(OverdueTasks > 1 ? "s" : "")}",
                $"You have {OverdueTasks} overdue task{(OverdueTasks > 1 ? "s" : "")} that need attention.",
                notifId++);
        }

        if (DueVaccinationCount > 0)
        {
            _notifications.ShowNotification(
                "Vaccinations Due",
                $"{DueVaccinationCount} animal(s) have vaccinations due.",
                notifId++);
        }

        if (SickCount > 0)
        {
            _notifications.ShowNotification(
                "Sick Animals",
                $"{SickCount} animal(s) are marked as sick.",
                notifId++);
        }

        var criticalAlerts = Alerts.Where(a =>
            a.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
            a.Severity.Equals("High", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var alert in criticalAlerts.Take(3))
        {
            _notifications.ShowNotification(
                $"⚠️ {alert.Title}",
                alert.Message,
                notifId++);
        }
    }

    public class DashboardSummary
    {
        public int TotalAnimals { get; set; }
        public int PregnantCount { get; set; }
        public int SickCount { get; set; }
        public int DueVaccinationCount { get; set; }
        public int OverdueTasks { get; set; }
        public int UpcomingBirths { get; set; }
    }

    public class AlertItem
    {
        public string AlertType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class SyncErrorDetail
    {
        public string EntityType { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string DisplayText => $"[{EntityType}] {ErrorMessage}";
    }
}
