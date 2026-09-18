using FMS.Application.Common;
using FMS.Application.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace FMS.Infrastructure.Configuration;

public class CachedConfigurationService : IConfigurationService
{
    private readonly IConfigurationService _inner;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private string CKey(Guid farmId, string e, Guid? scope = null) => $"config:{farmId}:{e}:{scope?.ToString() ?? "all"}";
    private string GenKey(Guid farmId, string baseEntity) => $"config-gen:{farmId}:{baseEntity}";

    public CachedConfigurationService(IConfigurationService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    /// <summary>
    /// Generation (cache-tag) counter for a farm + base entity (e.g. "BR").
    /// Reads embed the current generation in the data key; invalidation bumps the
    /// counter, which instantly orphans every cached entry for that farm + entity —
    /// including per-scope variants like "BR:{animalTypeId}" that a key removal
    /// could not target (IMemoryCache cannot enumerate keys for prefix deletion).
    /// Markers never expire: they are tiny (bounded by farms × entities), and if a
    /// marker expired while an old data entry still lived, the generation could
    /// reset to 0 and briefly resurface stale data.
    /// </summary>
    private int GetGeneration(Guid farmId, string baseEntity)
        => _cache.TryGetValue(GenKey(farmId, baseEntity), out int gen) ? gen : 0;

    private void BumpGeneration(Guid farmId, string baseEntity)
        => _cache.Set(GenKey(farmId, baseEntity), GetGeneration(farmId, baseEntity) + 1);

    /// <summary>Base entity of a cache entity string: "BR:{id}" → "BR", "AT" → "AT".</summary>
    private static string BaseEntity(string entity)
    {
        var idx = entity.IndexOf(':');
        return idx < 0 ? entity : entity[..idx];
    }

    private async Task<Result<T>> GetOrSet<T>(Guid farmId, string entity, Func<Task<Result<T>>> factory)
    {
        // Include the generation so any invalidation for this farm + base entity
        // makes the key unreachable immediately (old entries just age out via TTL).
        var key = CKey(farmId, $"{entity}:g{GetGeneration(farmId, BaseEntity(entity))}");
        if (_cache.TryGetValue(key, out T? cached) && cached != null) return Result<T>.Success(cached);
        var result = await factory();
        if (result.IsSuccess) _cache.Set(key, result.Value, CacheDuration);
        return result;
    }

    // Invalidates every cached list (all scopes, e.g. "BR", "BR:{animalTypeId}")
    // for this farm + entity by bumping the shared generation counter.
    private void Inv(Guid farmId, string entity) => BumpGeneration(farmId, BaseEntity(entity));

    // For mutations that reach beyond the entity that was touched (e.g. deleting an animal type
    // cascades its breeds away, and the animal-type list embeds its breeds).
    private void Inv(Guid farmId, params string[] entities)
    {
        foreach (var entity in entities) BumpGeneration(farmId, BaseEntity(entity));
    }

    public Task<Result<List<AnimalTypeDto>>> GetAnimalTypesAsync(Guid farmId) => GetOrSet(farmId, "AT", () => _inner.GetAnimalTypesAsync(farmId));
    public Task<Result<List<BreedDto>>> GetBreedsAsync(Guid farmId, Guid? animalTypeId = null) => GetOrSet(farmId, $"BR:{animalTypeId?.ToString() ?? "all"}", () => _inner.GetBreedsAsync(farmId, animalTypeId));
    public Task<Result<List<SexOptionDto>>> GetSexOptionsAsync(Guid farmId) => GetOrSet(farmId, "SX", () => _inner.GetSexOptionsAsync(farmId));
    public Task<Result<List<AgeCategoryDto>>> GetAgeCategoriesAsync(Guid farmId) => GetOrSet(farmId, "AC", () => _inner.GetAgeCategoriesAsync(farmId));
    public Task<Result<List<AnimalStatusDto>>> GetAnimalStatusesAsync(Guid farmId) => GetOrSet(farmId, "AS", () => _inner.GetAnimalStatusesAsync(farmId));
    public Task<Result<List<IdentificationTypeDto>>> GetIdentificationTypesAsync(Guid farmId) => GetOrSet(farmId, "IT", () => _inner.GetIdentificationTypesAsync(farmId));
    public Task<Result<List<LocationTypeDto>>> GetLocationTypesAsync(Guid farmId) => GetOrSet(farmId, "LT", () => _inner.GetLocationTypesAsync(farmId));
    public Task<Result<List<LocationDto>>> GetLocationsAsync(Guid farmId) => GetOrSet(farmId, "LO", () => _inner.GetLocationsAsync(farmId));
    public Task<Result<List<CustomFieldDefinitionDto>>> GetCustomFieldDefinitionsAsync(Guid farmId) => GetOrSet(farmId, "CF", () => _inner.GetCustomFieldDefinitionsAsync(farmId));
    public Task<Result<List<FarmConfigurationDto>>> GetFarmConfigurationsAsync(Guid farmId) => GetOrSet(farmId, "FC", () => _inner.GetFarmConfigurationsAsync(farmId));

    public async Task<Result<AnimalTypeDto>> CreateAnimalTypeAsync(Guid farmId, CreateAnimalTypeRequest r) { var res = await _inner.CreateAnimalTypeAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "AT"); return res; }
    // Deleting an animal type cascades its breeds away, so the cached breed lists must be dropped
    // with the animal-type list — otherwise the Breeds tab keeps listing rows that no longer exist.
    public async Task<Result> DeleteAnimalTypeAsync(Guid farmId, Guid id) { var res = await _inner.DeleteAnimalTypeAsync(farmId, id); Inv(farmId, "AT", "BR"); return res; }
    // Breed mutations refresh the animal-type list too: its DTO embeds the breeds and their count.
    public async Task<Result<BreedDto>> CreateBreedAsync(Guid farmId, CreateBreedRequest r) { var res = await _inner.CreateBreedAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "BR", "AT"); return res; }
    public async Task<Result> DeleteBreedAsync(Guid farmId, Guid id) { var res = await _inner.DeleteBreedAsync(farmId, id); Inv(farmId, "BR", "AT"); return res; }
    public async Task<Result<SexOptionDto>> CreateSexOptionAsync(Guid farmId, CreateSexOptionRequest r) { var res = await _inner.CreateSexOptionAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "SX"); return res; }
    public async Task<Result> DeleteSexOptionAsync(Guid farmId, Guid id) { var res = await _inner.DeleteSexOptionAsync(farmId, id); Inv(farmId, "SX"); return res; }
    public async Task<Result<AgeCategoryDto>> CreateAgeCategoryAsync(Guid farmId, CreateAgeCategoryRequest r) { var res = await _inner.CreateAgeCategoryAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "AC"); return res; }
    public async Task<Result> DeleteAgeCategoryAsync(Guid farmId, Guid id) { var res = await _inner.DeleteAgeCategoryAsync(farmId, id); Inv(farmId, "AC"); return res; }
    public async Task<Result<AnimalStatusDto>> CreateAnimalStatusAsync(Guid farmId, CreateAnimalStatusRequest r) { var res = await _inner.CreateAnimalStatusAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "AS"); return res; }
    public async Task<Result> DeleteAnimalStatusAsync(Guid farmId, Guid id) { var res = await _inner.DeleteAnimalStatusAsync(farmId, id); Inv(farmId, "AS"); return res; }
    public async Task<Result<IdentificationTypeDto>> CreateIdentificationTypeAsync(Guid farmId, CreateIdentificationTypeRequest r) { var res = await _inner.CreateIdentificationTypeAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "IT"); return res; }
    public async Task<Result> DeleteIdentificationTypeAsync(Guid farmId, Guid id) { var res = await _inner.DeleteIdentificationTypeAsync(farmId, id); Inv(farmId, "IT"); return res; }
    public async Task<Result<LocationTypeDto>> CreateLocationTypeAsync(Guid farmId, CreateLocationTypeRequest r) { var res = await _inner.CreateLocationTypeAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "LT"); return res; }
    public async Task<Result> DeleteLocationTypeAsync(Guid farmId, Guid id) { var res = await _inner.DeleteLocationTypeAsync(farmId, id); Inv(farmId, "LT"); return res; }
    public async Task<Result<LocationDto>> CreateLocationAsync(Guid farmId, CreateLocationRequest r) { var res = await _inner.CreateLocationAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "LO"); return res; }
    public async Task<Result> DeleteLocationAsync(Guid farmId, Guid id) { var res = await _inner.DeleteLocationAsync(farmId, id); Inv(farmId, "LO"); return res; }
    public async Task<Result<CustomFieldDefinitionDto>> CreateCustomFieldDefinitionAsync(Guid farmId, CreateCustomFieldDefinitionRequest r) { var res = await _inner.CreateCustomFieldDefinitionAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "CF"); return res; }
    public async Task<Result> DeleteCustomFieldDefinitionAsync(Guid farmId, Guid id) { var res = await _inner.DeleteCustomFieldDefinitionAsync(farmId, id); Inv(farmId, "CF"); return res; }
    public async Task<Result<FarmConfigurationDto>> UpsertFarmConfigurationAsync(Guid farmId, UpdateFarmConfigurationRequest r) { var res = await _inner.UpsertFarmConfigurationAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "FC"); return res; }
    public async Task<Result> DeleteFarmConfigurationAsync(Guid farmId, string key) { var res = await _inner.DeleteFarmConfigurationAsync(farmId, key); Inv(farmId, "FC"); return res; }
}
