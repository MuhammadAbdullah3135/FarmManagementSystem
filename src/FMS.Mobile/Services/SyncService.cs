using System.Net.Http.Json;
using System.Text.Json;
using FMS.Mobile.Models;

namespace FMS.Mobile.Services;

public class SyncService : ISyncService
{
    private readonly IDatabaseService _db;
    private readonly IApiService _api;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private const int MaxRetries = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DateTime? LastSyncTime { get; private set; }
    public Action? SyncCompleted { get; set; }
    public Action<int>? PendingCountChanged { get; set; }

    public SyncService(IDatabaseService db, IApiService api)
    {
        _db = db;
        _api = api;
    }

    // ── Core sync logic ────────────────────────────────

    public async Task<SyncResult> SyncPendingRecordsAsync(Guid farmId, IProgress<SyncProgress>? progress = null)
    {
        if (!await _syncLock.WaitAsync(0))
            return new SyncResult { Errors = { new SyncError { EntityType = "System", ErrorMessage = "Sync already in progress" } } };

        try
        {
            return await SyncRecordsInternalAsync(
                await _db.GetPendingRecordsAsync(farmId),
                farmId, progress);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<SyncResult> RetryFailedRecordsAsync(Guid farmId, IProgress<SyncProgress>? progress = null)
    {
        if (!await _syncLock.WaitAsync(0))
            return new SyncResult { Errors = { new SyncError { EntityType = "System", ErrorMessage = "Sync already in progress" } } };

        try
        {
            var failedRecords = await _db.GetFailedRecordsAsync();
            var farmRecords = failedRecords.Where(r => r.FarmId == farmId).ToList();

            // Reset failed records to pending so they get retried
            foreach (var record in farmRecords)
            {
                record.SyncStatus = SyncStatus.Pending;
                record.RetryCount = 0;
                record.LastError = null;
                await _db.UpdatePendingRecordAsync(record);
            }

            return await SyncRecordsInternalAsync(farmRecords, farmId, progress);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<SyncResult> SyncRecordsInternalAsync(List<PendingRecord> records, Guid farmId, IProgress<SyncProgress>? progress)
    {
        var result = new SyncResult();
        var total = records.Count;

        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            progress?.Report(new SyncProgress
            {
                Current = i + 1,
                Total = total,
                CurrentEntity = record.EntityType
            });

            try
            {
                record.SyncStatus = SyncStatus.Syncing;
                await _db.UpdatePendingRecordAsync(record);

                var serverId = await PushRecordAsync(record, farmId);
                if (serverId.HasValue)
                {
                    record.SyncStatus = SyncStatus.Synced;
                    record.ServerId = serverId;
                    record.RetryCount = 0;
                    record.LastError = null;
                    await _db.UpdatePendingRecordAsync(record);
                    await _db.SaveIdMappingAsync(record.Id, record.EntityType, serverId.Value, farmId);

                    // Clean up image file after successful upload
                    if (record.EntityType == "Image" && !string.IsNullOrEmpty(record.FilePath) && File.Exists(record.FilePath))
                    {
                        try { File.Delete(record.FilePath); }
                        catch { /* best effort cleanup */ }
                    }

                    result.Synced++;
                }
                else
                {
                    record.SyncStatus = SyncStatus.Failed;
                    record.LastError = "Server returned no ID";
                    record.RetryCount = MaxRetries;
                    await _db.UpdatePendingRecordAsync(record);
                    result.Failed++;
                    result.Errors.Add(new SyncError
                    {
                        RecordId = record.Id,
                        EntityType = record.EntityType,
                        ErrorMessage = "Server returned no ID"
                    });
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // Validation errors: extract server error message, don't retry
                var serverMessage = await ExtractErrorMessageAsync(ex);
                record.SyncStatus = SyncStatus.Failed;
                record.LastError = serverMessage;
                record.RetryCount = MaxRetries;
                await _db.UpdatePendingRecordAsync(record);
                result.Failed++;
                result.Errors.Add(new SyncError
                {
                    RecordId = record.Id,
                    EntityType = record.EntityType,
                    ErrorMessage = serverMessage
                });
            }
            catch (HttpRequestException ex) when (IsServerError(ex))
            {
                record.RetryCount++;
                if (record.RetryCount >= MaxRetries)
                {
                    record.SyncStatus = SyncStatus.Failed;
                    record.LastError = $"Server error: {ex.StatusCode}";
                    result.Failed++;
                    result.Errors.Add(new SyncError
                    {
                        RecordId = record.Id,
                        EntityType = record.EntityType,
                        ErrorMessage = $"Server error after {MaxRetries} retries: {ex.StatusCode}"
                    });
                }
                else
                {
                    record.SyncStatus = SyncStatus.Pending;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, record.RetryCount));
                    await Task.Delay(delay);
                }
                await _db.UpdatePendingRecordAsync(record);
            }
            catch (JsonException ex)
            {
                // Corrupted payload — can't recover
                record.SyncStatus = SyncStatus.Failed;
                record.LastError = $"Corrupted payload: {ex.Message}";
                record.RetryCount = MaxRetries;
                await _db.UpdatePendingRecordAsync(record);
                result.Failed++;
                result.Errors.Add(new SyncError
                {
                    RecordId = record.Id,
                    EntityType = record.EntityType,
                    ErrorMessage = $"Corrupted data — cannot sync: {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                record.SyncStatus = SyncStatus.Failed;
                record.LastError = ex.Message;
                await _db.UpdatePendingRecordAsync(record);
                result.Failed++;
                result.Errors.Add(new SyncError
                {
                    RecordId = record.Id,
                    EntityType = record.EntityType,
                    ErrorMessage = ex.Message
                });
            }
        }

        // Clean up synced records (and their image files)
        await CleanupSyncedRecordsAsync();
        LastSyncTime = DateTime.UtcNow;
        SyncCompleted?.Invoke();

        var remaining = await _db.GetPendingCountAsync(farmId);
        PendingCountChanged?.Invoke(remaining);

        return result;
    }

    // ── Push record to API ─────────────────────────────

    private async Task<Guid?> PushRecordAsync(PendingRecord record, Guid farmId)
    {
        if (record.EntityType == "Image")
            return await PushImageAsync(record);

        if (record.EntityType == "TaskComplete")
            return await PushTaskCompleteAsync(record, farmId);

        // WeightRecord uses animal-specific endpoint
        string endpoint;
        if (record.EntityType == "WeightRecord")
        {
            var payloadCheck = JsonSerializer.Deserialize<JsonElement>(record.Payload, JsonOptions);
            var animalIdValue = payloadCheck.GetProperty("animalId").GetString();
            if (!Guid.TryParse(animalIdValue, out var localAnimalId))
                throw new InvalidOperationException("WeightRecord payload contains an invalid animal ID.");
            var animalId = await ResolveServerIdAsync(localAnimalId, "Animal", farmId);
            endpoint = $"/farm/{record.FarmId}/animals/{animalId}/weights";
        }
        else
        {
            endpoint = GetEndpoint(record.EntityType, record.FarmId);
        }

        var payload = JsonSerializer.Deserialize<JsonElement>(record.Payload, JsonOptions);
        payload = await ResolvePayloadReferencesAsync(payload, farmId);
        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), System.Text.Encoding.UTF8, "application/json");
        var response = await _api.Client.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseBody))
            throw new InvalidOperationException($"Server response for {record.EntityType} did not contain an ID.");

        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            return Guid.Parse(idProp.GetString()!);
        if (doc.RootElement.TryGetProperty("Id", out var idProp2) && idProp2.ValueKind == JsonValueKind.String)
            return Guid.Parse(idProp2.GetString()!);
        if (doc.RootElement.TryGetProperty("id", out var idNum) && idNum.ValueKind == JsonValueKind.Number)
            return Guid.Parse(idNum.ToString());

        throw new InvalidOperationException($"Server response for {record.EntityType} did not contain an ID.");
    }

    private async Task<Guid?> PushImageAsync(PendingRecord record)
    {
        if (string.IsNullOrEmpty(record.FilePath) || !File.Exists(record.FilePath))
            throw new FileNotFoundException($"Queued image file not found: {record.FilePath}");

        var payload = JsonSerializer.Deserialize<ImagePayload>(record.Payload, JsonOptions);
        if (payload is null) throw new InvalidOperationException("Image payload is null");

        var endpoint = $"/farm/{record.FarmId}/animals/{payload.AnimalId}/images";

        using var fileStream = File.OpenRead(record.FilePath);
        var result = await _api.PostMultipartAsync<ImageResponse>(
            endpoint, fileStream, Path.GetFileName(record.FilePath), "image/jpeg",
            new Dictionary<string, string> { ["caption"] = payload.Caption ?? "", ["isPrimary"] = "false" });

        return result?.Id ?? throw new InvalidOperationException("Server response for Image did not contain an ID.");
    }

    private async Task<Guid?> PushTaskCompleteAsync(PendingRecord record, Guid farmId)
    {
        var payload = JsonSerializer.Deserialize<TaskCompletePayload>(record.Payload, JsonOptions);
        if (payload is null) throw new InvalidOperationException("TaskComplete payload is null");

        var taskId = await ResolveServerIdAsync(payload.TaskId, "Task", farmId);
        var endpoint = $"/farm/{record.FarmId}/tasks/{taskId}/complete";
        await _api.PostAsync(endpoint, new { completionNotes = "" });

        // Task completion doesn't return an ID — use the taskId as server ID
        return taskId;
    }

    private async Task<Guid> ResolveServerIdAsync(Guid localId, string entityType, Guid farmId)
    {
        var mapping = await _db.ResolveServerIdAsync(localId, entityType);
        if (mapping is null || mapping.FarmId != farmId)
            return localId;
        return mapping.ServerId;
    }

    private async Task<JsonElement> ResolvePayloadReferencesAsync(JsonElement payload, Guid farmId)
    {
        if (payload.ValueKind != JsonValueKind.Object) return payload;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (property.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String && Guid.TryParse(property.Value.GetString(), out var localId))
                    writer.WriteStringValue(await ResolveServerIdAsync(localId, property.Name[..^2], farmId));
                else
                    property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    // ── Endpoint mapping ───────────────────────────────

    private static string GetEndpoint(string entityType, Guid farmId) => entityType switch
    {
        "FeedRecord" => $"/farm/{farmId}/feed/records",
        "MedicalRecord" => $"/farm/{farmId}/medical-records",
        "VaccinationRecord" => $"/farm/{farmId}/vaccinations",
        "BreedingRecord" => $"/farm/{farmId}/breeding-records",
        "Animal" => $"/farm/{farmId}/animals",
        _ => throw new InvalidOperationException($"Unknown entity type: {entityType}")
    };

    // ── File cleanup ───────────────────────────────────

    private async Task CleanupSyncedRecordsAsync()
    {
        var syncedRecords = await _db.GetSyncedRecordsAsync();
        foreach (var record in syncedRecords)
        {
            if (!string.IsNullOrEmpty(record.FilePath) && File.Exists(record.FilePath))
            {
                try { File.Delete(record.FilePath); }
                catch { /* best effort */ }
            }
        }
        await _db.DeleteSyncedRecordsAsync();
    }

    // ── Error extraction ───────────────────────────────

    private static async Task<string> ExtractErrorMessageAsync(HttpRequestException ex)
    {
        try
        {
            if (ex.Data.Contains("ResponseBody"))
                return ex.Data["ResponseBody"]?.ToString() ?? ex.Message;
        }
        catch { }
        return ex.Message;
    }

    private static bool IsServerError(HttpRequestException ex) =>
        ex.StatusCode is >= System.Net.HttpStatusCode.InternalServerError
            and < System.Net.HttpStatusCode.HttpVersionNotSupported;

    // ── Farm animal cache sync ─────────────────────────

    public async Task SyncFarmAnimalsAsync(Guid farmId, IProgress<int>? progress = null)
    {
        await _db.ClearCachedAnimalsAsync(farmId);
        var allAnimals = new List<Models.CachedAnimal>();
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            var endpoint = $"/farm/{farmId}/animals?page={page}&pageSize={pageSize}";
            var response = await _api.Client.GetAsync(endpoint);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("items", out var items)) break;
            var count = items.GetArrayLength();
            if (count == 0) break;

            foreach (var item in items.EnumerateArray())
            {
                allAnimals.Add(new Models.CachedAnimal
                {
                    Id = Guid.Parse(item.GetProperty("id").GetString()!),
                    FarmId = farmId,
                    TagNumber = item.TryGetProperty("tagNumber", out var t) ? t.GetString() ?? "" : "",
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() : null,
                    AnimalTypeName = item.TryGetProperty("animalTypeName", out var at) ? at.GetString() ?? "" : "",
                    BreedName = item.TryGetProperty("breedName", out var b) ? b.GetString() : null,
                    SexValue = item.TryGetProperty("sexValue", out var s) ? s.GetString() ?? "" : "",
                    StatusName = item.TryGetProperty("statusName", out var st) ? st.GetString() ?? "" : "",
                    LocationName = item.TryGetProperty("locationName", out var l) ? l.GetString() : null,
                    LatestWeightKg = item.TryGetProperty("latestWeightKg", out var w) && w.ValueKind != JsonValueKind.Null
                        ? w.GetDecimal() : null
                });
            }

            progress?.Report(allAnimals.Count);
            if (count < pageSize) break;
            page++;
        }

        await _db.UpsertCachedAnimalsAsync(allAnimals);
    }

    // ── Inner types ────────────────────────────────────

    private class ImagePayload { public Guid AnimalId { get; set; } public string? Caption { get; set; } }
    private class ImageResponse { public Guid Id { get; set; } }
    private class TaskCompletePayload { public Guid TaskId { get; set; } }
}
