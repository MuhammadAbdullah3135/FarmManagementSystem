using System.Text.Json;
using FMS.Mobile.Models;

namespace FMS.Mobile.Services;

public class ReferenceDataService : IReferenceDataService
{
    private readonly IDatabaseService _db;
    private readonly IApiService _api;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ReferenceDataService(IDatabaseService db, IApiService api)
    {
        _db = db;
        _api = api;
    }

    public Task<List<CachedReferenceData>> GetAnimalTypesAsync(Guid farmId)
        => _db.GetReferenceDataAsync(farmId, "AnimalType");

    public Task<List<CachedReferenceData>> GetBreedsAsync(Guid farmId)
        => _db.GetReferenceDataAsync(farmId, "Breed");

    public Task<List<CachedReferenceData>> GetFeedTypesAsync(Guid farmId)
        => _db.GetReferenceDataAsync(farmId, "FeedType");

    public Task<List<CachedReferenceData>> GetVaccineTypesAsync(Guid farmId)
        => _db.GetReferenceDataAsync(farmId, "VaccineType");

    public Task<List<CachedReferenceData>> GetAnimalStatusesAsync(Guid farmId)
        => _db.GetReferenceDataAsync(farmId, "AnimalStatus");

    public Task<List<CachedReferenceData>> GetLocationsAsync(Guid farmId)
        => _db.GetReferenceDataAsync(farmId, "Location");

    public async Task SyncAllReferenceDataAsync(Guid farmId)
    {
        _api.SetFarmHeader(farmId);

        // Animal Types
        try
        {
            var types = await _api.GetAsync<List<ApiReferenceItem>>($"/farm/{farmId}/configuration/animal-types");
            if (types is not null)
            {
                var cached = types.Select(t => new CachedReferenceData
                {
                    DataId = t.Id, Name = t.Name,
                    ExtraJson = JsonSerializer.Serialize(t, JsonOptions)
                });
                await _db.UpsertReferenceDataAsync(farmId, "AnimalType", cached);
            }
        }
        catch { /* best-effort */ }

        // Breeds
        try
        {
            var breeds = await _api.GetAsync<List<ApiReferenceItem>>($"/farm/{farmId}/configuration/breeds");
            if (breeds is not null)
            {
                var cached = breeds.Select(b => new CachedReferenceData
                {
                    DataId = b.Id, Name = b.Name,
                    ExtraJson = JsonSerializer.Serialize(b, JsonOptions)
                });
                await _db.UpsertReferenceDataAsync(farmId, "Breed", cached);
            }
        }
        catch { /* best-effort */ }

        // Feed Types
        try
        {
            var feedTypes = await _api.GetAsync<List<ApiFeedTypeItem>>($"/farm/{farmId}/feed/types");
            if (feedTypes is not null)
            {
                var cached = feedTypes.Select(f => new CachedReferenceData
                {
                    DataId = f.Id, Name = f.Name,
                    ExtraJson = JsonSerializer.Serialize(f, JsonOptions)
                });
                await _db.UpsertReferenceDataAsync(farmId, "FeedType", cached);
            }
        }
        catch { /* best-effort */ }

        // Vaccine Types
        try
        {
            var vaccines = await _api.GetAsync<List<ApiVaccineTypeItem>>($"/farm/{farmId}/vaccines?pageSize=100");
            if (vaccines is not null)
            {
                var cached = vaccines.Select(v => new CachedReferenceData
                {
                    DataId = v.Id, Name = v.Name,
                    ExtraJson = JsonSerializer.Serialize(v, JsonOptions)
                });
                await _db.UpsertReferenceDataAsync(farmId, "VaccineType", cached);
            }
        }
        catch { /* best-effort */ }

        // Animal Statuses
        try
        {
            var statuses = await _api.GetAsync<List<ApiReferenceItem>>($"/farm/{farmId}/configuration/animal-statuses");
            if (statuses is not null)
            {
                var cached = statuses.Select(s => new CachedReferenceData
                {
                    DataId = s.Id, Name = s.Name,
                    ExtraJson = JsonSerializer.Serialize(s, JsonOptions)
                });
                await _db.UpsertReferenceDataAsync(farmId, "AnimalStatus", cached);
            }
        }
        catch { /* best-effort */ }

        // Locations
        try
        {
            var locations = await _api.GetAsync<List<ApiReferenceItem>>($"/farm/{farmId}/configuration/locations");
            if (locations is not null)
            {
                var cached = locations.Select(l => new CachedReferenceData
                {
                    DataId = l.Id, Name = l.Name,
                    ExtraJson = JsonSerializer.Serialize(l, JsonOptions)
                });
                await _db.UpsertReferenceDataAsync(farmId, "Location", cached);
            }
        }
        catch { /* best-effort */ }
    }

    // API response types for reference data
    private class ApiReferenceItem { public Guid Id { get; set; } public string Name { get; set; } = ""; }
    private class ApiFeedTypeItem { public Guid Id { get; set; } public string Name { get; set; } = ""; }
    private class ApiVaccineTypeItem { public Guid Id { get; set; } public string Name { get; set; } = ""; }
}
