using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace FMS.Domain.Tests;

/// <summary>
/// CachedConfigurationService: per-animal-type breed filtering must not return stale cross-type data.
/// </summary>
public class ConfigurationCacheTests
{
    private sealed class FakeInner : IConfigurationService
    {
        public Guid TypeA = Guid.NewGuid();
        public Guid TypeB = Guid.NewGuid();
        public int CallCount;

        public Task<Result<List<BreedDto>>> GetBreedsAsync(Guid farmId, Guid? animalTypeId = null)
        {
            CallCount++;
            var breed = new BreedDto
            {
                Id = Guid.NewGuid(),
                Name = animalTypeId == TypeA ? "Breed-A" : "Breed-B",
                AnimalTypeId = animalTypeId ?? Guid.Empty
            };
            return Task.FromResult(Result<List<BreedDto>>.Success(new List<BreedDto> { breed }));
        }

        #region Not-under-test (no-op)
        public Task<Result<List<AnimalTypeDto>>> GetAnimalTypesAsync(Guid farmId) => Task.FromResult(Result<List<AnimalTypeDto>>.Success(new()));
        public Task<Result<List<SexOptionDto>>> GetSexOptionsAsync(Guid farmId) => Task.FromResult(Result<List<SexOptionDto>>.Success(new()));
        public Task<Result<List<AgeCategoryDto>>> GetAgeCategoriesAsync(Guid farmId) => Task.FromResult(Result<List<AgeCategoryDto>>.Success(new()));
        public Task<Result<List<AnimalStatusDto>>> GetAnimalStatusesAsync(Guid farmId) => Task.FromResult(Result<List<AnimalStatusDto>>.Success(new()));
        public Task<Result<List<IdentificationTypeDto>>> GetIdentificationTypesAsync(Guid farmId) => Task.FromResult(Result<List<IdentificationTypeDto>>.Success(new()));
        public Task<Result<List<LocationTypeDto>>> GetLocationTypesAsync(Guid farmId) => Task.FromResult(Result<List<LocationTypeDto>>.Success(new()));
        public Task<Result<List<LocationDto>>> GetLocationsAsync(Guid farmId) => Task.FromResult(Result<List<LocationDto>>.Success(new()));
        public Task<Result<List<CustomFieldDefinitionDto>>> GetCustomFieldDefinitionsAsync(Guid farmId) => Task.FromResult(Result<List<CustomFieldDefinitionDto>>.Success(new()));
        public Task<Result<List<FarmConfigurationDto>>> GetFarmConfigurationsAsync(Guid farmId) => Task.FromResult(Result<List<FarmConfigurationDto>>.Success(new()));
        public Task<Result<AnimalTypeDto>> CreateAnimalTypeAsync(Guid farmId, CreateAnimalTypeRequest r) => Task.FromResult(Result<AnimalTypeDto>.Success(new()));
        public Task<Result> DeleteAnimalTypeAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<BreedDto>> CreateBreedAsync(Guid farmId, CreateBreedRequest r) => Task.FromResult(Result<BreedDto>.Success(new()));
        public Task<Result> DeleteBreedAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<SexOptionDto>> CreateSexOptionAsync(Guid farmId, CreateSexOptionRequest r) => Task.FromResult(Result<SexOptionDto>.Success(new()));
        public Task<Result> DeleteSexOptionAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<AgeCategoryDto>> CreateAgeCategoryAsync(Guid farmId, CreateAgeCategoryRequest r) => Task.FromResult(Result<AgeCategoryDto>.Success(new()));
        public Task<Result> DeleteAgeCategoryAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<AnimalStatusDto>> CreateAnimalStatusAsync(Guid farmId, CreateAnimalStatusRequest r) => Task.FromResult(Result<AnimalStatusDto>.Success(new()));
        public Task<Result> DeleteAnimalStatusAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<IdentificationTypeDto>> CreateIdentificationTypeAsync(Guid farmId, CreateIdentificationTypeRequest r) => Task.FromResult(Result<IdentificationTypeDto>.Success(new()));
        public Task<Result> DeleteIdentificationTypeAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<LocationTypeDto>> CreateLocationTypeAsync(Guid farmId, CreateLocationTypeRequest r) => Task.FromResult(Result<LocationTypeDto>.Success(new()));
        public Task<Result> DeleteLocationTypeAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<LocationDto>> CreateLocationAsync(Guid farmId, CreateLocationRequest r) => Task.FromResult(Result<LocationDto>.Success(new()));
        public Task<Result> DeleteLocationAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<CustomFieldDefinitionDto>> CreateCustomFieldDefinitionAsync(Guid farmId, CreateCustomFieldDefinitionRequest r) => Task.FromResult(Result<CustomFieldDefinitionDto>.Success(new()));
        public Task<Result> DeleteCustomFieldDefinitionAsync(Guid farmId, Guid id) => Task.FromResult(Result.Success());
        public Task<Result<FarmConfigurationDto>> UpsertFarmConfigurationAsync(Guid farmId, UpdateFarmConfigurationRequest r) => Task.FromResult(Result<FarmConfigurationDto>.Success(new()));
        public Task<Result> DeleteFarmConfigurationAsync(Guid farmId, string key) => Task.FromResult(Result.Success());
        #endregion
    }

    [Fact]
    public async Task Breeds_ByAnimalType_ShouldNotReturn_StaleCached_OtherType()
    {
        var inner = new FakeInner();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new CachedConfigurationService(inner, cache);
        var farmId = Guid.NewGuid();

        var typeA = await svc.GetBreedsAsync(farmId, inner.TypeA);
        Assert.True(typeA.IsSuccess);
        Assert.Equal("Breed-A", typeA.Value![0].Name);

        var typeB = await svc.GetBreedsAsync(farmId, inner.TypeB);

        // EXPECTED: type B returns its own breed ("Breed-B").
        // ACTUAL (verified bug): cache key ignores animalTypeId -> returns cached "Breed-A".
        Assert.Equal("Breed-B", typeB.Value![0].Name);
    }
}
