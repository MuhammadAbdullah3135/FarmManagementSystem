using SQLite;

namespace FMS.Mobile.Models;

public enum SyncStatus
{
    Pending,
    Syncing,
    Failed,
    Synced
}

public class PendingRecord
{
    [PrimaryKey]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public Guid FarmId { get; set; }
    public string Payload { get; set; } = string.Empty;

    /// <summary>File path for image uploads (null for non-file records).</summary>
    public string? FilePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public Guid? ServerId { get; set; }
}

public class CachedAnimal
{
    [PrimaryKey]
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string AnimalTypeName { get; set; } = string.Empty;
    public string? BreedName { get; set; }
    public string SexValue { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public int StatusCategory { get; set; }
    public string? LocationName { get; set; }
    public decimal? LatestWeightKg { get; set; }
}

/// <summary>
/// Generic reference data cache for dropdowns (animal types, breeds, feed types, vaccine types, etc.).
/// </summary>
public class CachedReferenceData
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public Guid FarmId { get; set; }

    /// <summary>e.g. "AnimalType", "Breed", "FeedType", "VaccineType", "AnimalStatus"</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid DataId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ExtraJson { get; set; }
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
}

public class LocalToServerIdMap
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public Guid LocalId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid ServerId { get; set; }
    public Guid FarmId { get; set; }
}
