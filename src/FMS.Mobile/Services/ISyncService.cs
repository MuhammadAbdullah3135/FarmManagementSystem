namespace FMS.Mobile.Services;

public interface ISyncService
{
    Task<SyncResult> SyncPendingRecordsAsync(Guid farmId, IProgress<SyncProgress>? progress = null);
    Task<SyncResult> RetryFailedRecordsAsync(Guid farmId, IProgress<SyncProgress>? progress = null);
    Task SyncFarmAnimalsAsync(Guid farmId, IProgress<int>? progress = null);
    DateTime? LastSyncTime { get; }

    Action? SyncCompleted { get; set; }
    Action<int>? PendingCountChanged { get; set; }
}

public class SyncResult
{
    public int Synced { get; set; }
    public int Failed { get; set; }
    public List<SyncError> Errors { get; set; } = new();
}

public class SyncError
{
    public Guid RecordId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class SyncProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentEntity { get; set; } = string.Empty;
}
