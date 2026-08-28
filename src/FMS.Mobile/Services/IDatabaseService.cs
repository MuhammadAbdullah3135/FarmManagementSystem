using FMS.Mobile.Models;
using SQLite;

namespace FMS.Mobile.Services;

public interface IDatabaseService
{
    Task InitializeAsync();

    // PendingRecord
    Task<List<PendingRecord>> GetPendingRecordsAsync(Guid? farmId = null);
    Task<List<PendingRecord>> GetFailedRecordsAsync();
    Task<List<PendingRecord>> GetSyncedRecordsAsync();
    Task<int> GetPendingCountAsync(Guid farmId);
    Task InsertPendingRecordAsync(PendingRecord record);
    Task UpdatePendingRecordAsync(PendingRecord record);
    Task DeletePendingRecordAsync(Guid id);
    Task DeleteSyncedRecordsAsync();

    // CachedAnimal
    Task<List<CachedAnimal>> GetCachedAnimalsAsync(Guid farmId, string? search = null);
    Task<CachedAnimal?> GetCachedAnimalByIdAsync(Guid id);
    Task<CachedAnimal?> GetCachedAnimalByTagAsync(Guid farmId, string tagNumber);
    Task UpsertCachedAnimalsAsync(IEnumerable<CachedAnimal> animals);
    Task ClearCachedAnimalsAsync(Guid farmId);

    // CachedReferenceData
    Task<List<CachedReferenceData>> GetReferenceDataAsync(Guid farmId, string entityType);
    Task UpsertReferenceDataAsync(Guid farmId, string entityType, IEnumerable<CachedReferenceData> items);
    Task ClearReferenceDataAsync(Guid farmId);

    // LocalToServerIdMap
    Task<LocalToServerIdMap?> ResolveServerIdAsync(Guid localId, string entityType);
    Task SaveIdMappingAsync(Guid localId, string entityType, Guid serverId, Guid farmId);
    Task ClearIdMappingsAsync();
    Task ClearAllAsync();
}
