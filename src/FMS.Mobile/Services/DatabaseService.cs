using FMS.Mobile.Models;
using SQLite;

namespace FMS.Mobile.Services;

public class DatabaseService : IDatabaseService
{
    private SQLiteAsyncConnection? _db;

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_db is not null) return _db;

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "fms_mobile.db3");
        _db = new SQLiteAsyncConnection(dbPath);

        await _db.CreateTableAsync<PendingRecord>();
        await _db.CreateTableAsync<CachedAnimal>();
        await _db.CreateTableAsync<LocalToServerIdMap>();
        await _db.CreateTableAsync<CachedReferenceData>();

        // Create indexes for performance
        await _db.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_PendingRecord_FarmId_SyncStatus ON PendingRecord(FarmId, SyncStatus)");
        await _db.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_PendingRecord_CreatedAt ON PendingRecord(CreatedAt)");
        await _db.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_CachedAnimal_FarmId_TagNumber ON CachedAnimal(FarmId, TagNumber)");
        await _db.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_CachedAnimal_FarmId_Name ON CachedAnimal(FarmId, Name)");
        await _db.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_LocalToServerIdMap_LocalId_EntityType ON LocalToServerIdMap(LocalId, EntityType)");
        await _db.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_CachedReferenceData_FarmId_EntityType ON CachedReferenceData(FarmId, EntityType)");

        return _db;
    }

    public async Task InitializeAsync() => await GetConnectionAsync();

    // ── PendingRecord ──────────────────────────────────

    public async Task<List<PendingRecord>> GetPendingRecordsAsync(Guid? farmId = null)
    {
        var db = await GetConnectionAsync();
        if (farmId.HasValue)
            return await db.Table<PendingRecord>()
                .Where(r => r.FarmId == farmId.Value && r.SyncStatus != SyncStatus.Synced)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();
        return await db.Table<PendingRecord>()
            .Where(r => r.SyncStatus != SyncStatus.Synced)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<PendingRecord>> GetFailedRecordsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<PendingRecord>()
            .Where(r => r.SyncStatus == SyncStatus.Failed)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<PendingRecord>> GetSyncedRecordsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<PendingRecord>()
            .Where(r => r.SyncStatus == SyncStatus.Synced)
            .ToListAsync();
    }

    public async Task<int> GetPendingCountAsync(Guid farmId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<PendingRecord>()
            .Where(r => r.FarmId == farmId && r.SyncStatus != SyncStatus.Synced)
            .CountAsync();
    }

    public async Task InsertPendingRecordAsync(PendingRecord record)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(record);
    }

    public async Task UpdatePendingRecordAsync(PendingRecord record)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(record);
    }

    public async Task DeletePendingRecordAsync(Guid id)
    {
        var db = await GetConnectionAsync();
        await db.Table<PendingRecord>().Where(r => r.Id == id).DeleteAsync();
    }

    public async Task DeleteSyncedRecordsAsync()
    {
        var db = await GetConnectionAsync();
        await db.Table<PendingRecord>().Where(r => r.SyncStatus == SyncStatus.Synced).DeleteAsync();
    }

    // ── CachedAnimal ───────────────────────────────────

    public async Task<List<CachedAnimal>> GetCachedAnimalsAsync(Guid farmId, string? search = null)
    {
        var db = await GetConnectionAsync();
        var query = db.Table<CachedAnimal>().Where(a => a.FarmId == farmId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLowerInvariant();
            query = query.Where(a => a.TagNumber.ToLower().Contains(term)
                || (a.Name != null && a.Name.ToLower().Contains(term)));
        }
        return await query.OrderBy(a => a.TagNumber).ToListAsync();
    }

    public async Task<CachedAnimal?> GetCachedAnimalByIdAsync(Guid id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<CachedAnimal>().FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<CachedAnimal?> GetCachedAnimalByTagAsync(Guid farmId, string tagNumber)
    {
        var db = await GetConnectionAsync();
        return await db.Table<CachedAnimal>()
            .FirstOrDefaultAsync(a => a.FarmId == farmId && a.TagNumber == tagNumber);
    }

    public async Task UpsertCachedAnimalsAsync(IEnumerable<CachedAnimal> animals)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(tran =>
        {
            foreach (var animal in animals)
            {
                var existing = tran.Table<CachedAnimal>().FirstOrDefault(a => a.Id == animal.Id);
                if (existing is not null)
                {
                    animal.Id = existing.Id;
                    tran.Update(animal);
                }
                else
                {
                    tran.Insert(animal);
                }
            }
        });
    }

    public async Task ClearCachedAnimalsAsync(Guid farmId)
    {
        var db = await GetConnectionAsync();
        await db.Table<CachedAnimal>().Where(a => a.FarmId == farmId).DeleteAsync();
    }

    // ── CachedReferenceData ────────────────────────────

    public async Task<List<CachedReferenceData>> GetReferenceDataAsync(Guid farmId, string entityType)
    {
        var db = await GetConnectionAsync();
        return await db.Table<CachedReferenceData>()
            .Where(r => r.FarmId == farmId && r.EntityType == entityType)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task UpsertReferenceDataAsync(Guid farmId, string entityType, IEnumerable<CachedReferenceData> items)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(tran =>
        {
            // Clear existing for this farm + type
            var existing = tran.Table<CachedReferenceData>()
                .Where(r => r.FarmId == farmId && r.EntityType == entityType)
                .ToList();
            foreach (var e in existing) tran.Delete(e);

            // Insert new
            foreach (var item in items)
            {
                item.FarmId = farmId;
                item.EntityType = entityType;
                item.LastSyncedAt = DateTime.UtcNow;
                tran.Insert(item);
            }
        });
    }

    public async Task ClearReferenceDataAsync(Guid farmId)
    {
        var db = await GetConnectionAsync();
        await db.Table<CachedReferenceData>().Where(r => r.FarmId == farmId).DeleteAsync();
    }

    // ── LocalToServerIdMap ─────────────────────────────

    public async Task<LocalToServerIdMap?> ResolveServerIdAsync(Guid localId, string entityType)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalToServerIdMap>()
            .FirstOrDefaultAsync(m => m.LocalId == localId && m.EntityType == entityType);
    }

    public async Task SaveIdMappingAsync(Guid localId, string entityType, Guid serverId, Guid farmId)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(new LocalToServerIdMap
        {
            LocalId = localId,
            EntityType = entityType,
            ServerId = serverId,
            FarmId = farmId
        });
    }

    public async Task ClearIdMappingsAsync()
    {
        var db = await GetConnectionAsync();
        await db.DeleteAllAsync<LocalToServerIdMap>();
    }

    public async Task ClearAllAsync()
    {
        var db = await GetConnectionAsync();
        await db.DeleteAllAsync<PendingRecord>();
        await db.DeleteAllAsync<CachedAnimal>();
        await db.DeleteAllAsync<LocalToServerIdMap>();
        await db.DeleteAllAsync<CachedReferenceData>();
    }
}
