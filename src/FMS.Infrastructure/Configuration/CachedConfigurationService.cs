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

    public CachedConfigurationService(IConfigurationService inner, IMemoryCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    private async Task<Result<T>> GetOrSet<T>(Guid farmId, string entity, Func<Task<Result<T>>> factory)
    {
        var key = CKey(farmId, entity);
        if (_cache.TryGetValue(key, out T? cached) && cached != null) return Result<T>.Success(cached);
        var result = await factory();
        if (result.IsSuccess) _cache.Set(key, result.Value, CacheDuration);
        return result;
    }

    private void Inv(Guid farmId, string entity) => _cache.Remove(CKey(farmId, entity));

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
    public async Task<Result> DeleteAnimalTypeAsync(Guid farmId, Guid id) { var res = await _inner.DeleteAnimalTypeAsync(farmId, id); Inv(farmId, "AT"); return res; }
    public async Task<Result<BreedDto>> CreateBreedAsync(Guid farmId, CreateBreedRequest r) { var res = await _inner.CreateBreedAsync(farmId, r); if (res.IsSuccess) Inv(farmId, "BR"); return res; }
    public async Task<Result> DeleteBreedAsync(Guid farmId, Guid id) { var res = await _inner.DeleteBreedAsync(farmId, id); Inv(farmId, "BR"); return res; }
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
