using CommunityToolkit.Mvvm.ComponentModel;
using FMS.Mobile.Services;

namespace FMS.Mobile.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    private IConnectivityService? _connectivity;
    private IDatabaseService? _db;
    private IAuthService? _auth;
    private ISyncService? _sync;
    private IReferenceDataService? _refData;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isOffline;
    [ObservableProperty] private int _pendingSyncCount;
    [ObservableProperty] private DateTime? _lastSyncTime;
    [ObservableProperty] private string? _syncStatusText;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string? _syncProgressText;

    public bool ShowQueueWarning => PendingSyncCount > 100;
    public bool HasFailedRecords => FailedCount > 0;
    public bool StatusIsError => FailedCount > 0;

    public string LastSyncText => LastSyncTime.HasValue
        ? $"Last sync: {LastSyncTime:HH:mm} · {PendingSyncCount} pending"
        : $"Not yet synced · {PendingSyncCount} pending";

    protected void InitializeSyncServices(IConnectivityService connectivity, IDatabaseService db, IAuthService auth, ISyncService sync)
    {
        _connectivity = connectivity;
        _db = db;
        _auth = auth;

        IsOffline = !connectivity.IsConnected;
        connectivity.ConnectivityChanged += (_, connected) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOffline = !connected;
                OnPropertyChanged(nameof(LastSyncText));
            });
        };

        sync.PendingCountChanged += count =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PendingSyncCount = count;
                OnPropertyChanged(nameof(ShowQueueWarning));
                OnPropertyChanged(nameof(LastSyncText));
            });
        };

        sync.SyncCompleted += () =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LastSyncTime = sync.LastSyncTime;
                OnPropertyChanged(nameof(LastSyncText));
                IsSyncing = false;
            });
        };
    }

    protected void InitializeSyncCommands(ISyncService sync, IReferenceDataService? refData = null)
    {
        _sync = sync;
        _refData = refData;
    }

    protected async Task RefreshSyncStatusAsync()
    {
        if (_db == null || _auth == null) return;
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        PendingSyncCount = await _db.GetPendingCountAsync(farmId.Value);
        OnPropertyChanged(nameof(ShowQueueWarning));
        OnPropertyChanged(nameof(LastSyncText));
    }

    /// <summary>Shared sync-now logic. Subclasses should call this from their own SyncNowCommand.</summary>
    protected async Task ExecuteSyncNowAsync()
    {
        if (_sync == null || _auth == null) return;
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        IsSyncing = true;
        SyncProgressText = "Syncing...";
        SyncStatusText = null;

        var progress = new Progress<SyncProgress>(p =>
        {
            SyncProgressText = $"Syncing {p.Current}/{p.Total}: {p.CurrentEntity}";
        });

        try
        {
            var result = await _sync.SyncPendingRecordsAsync(farmId.Value, progress);
            await _sync.SyncFarmAnimalsAsync(farmId.Value);
            if (_refData != null)
                await _refData.SyncAllReferenceDataAsync(farmId.Value);

            PendingSyncCount = await _db!.GetPendingCountAsync(farmId.Value);
            FailedCount = result.Failed;
            LastSyncTime = _sync.LastSyncTime;

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
    }

    /// <summary>Shared retry-failed logic. Subclasses should call this from their own RetryFailedCommand.</summary>
    protected async Task ExecuteRetryFailedAsync()
    {
        if (_sync == null || _auth == null) return;
        var farmId = _auth.ActiveFarmId;
        if (!farmId.HasValue) return;

        IsSyncing = true;
        SyncProgressText = "Retrying failed records...";

        try
        {
            var result = await _sync.RetryFailedRecordsAsync(farmId.Value);
            PendingSyncCount = await _db!.GetPendingCountAsync(farmId.Value);
            FailedCount = result.Failed;
            LastSyncTime = _sync.LastSyncTime;

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
    }

    protected void RunBusy(Action action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { action(); }
        finally { IsBusy = false; }
    }

    protected async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        finally { IsBusy = false; }
    }

    partial void OnPendingSyncCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowQueueWarning));
        OnPropertyChanged(nameof(LastSyncText));
    }

    partial void OnFailedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasFailedRecords));
        OnPropertyChanged(nameof(StatusIsError));
    }

    partial void OnLastSyncTimeChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(LastSyncText));
    }
}
