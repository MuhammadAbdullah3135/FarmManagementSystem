using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace FMS.Domain.Tests;

/// <summary>
/// CachedConfigurationService: per-animal-type breed filtering must not return stale
/// cross-type data, and breed create/delete must invalidate cached breed lists
/// (scoped AND unscoped) immediately, not just after cache TTL expiry.
/// </summary>
public class ConfigurationCacheTests
{
    private sealed class FakeInner : IConfigurationService
    {
        public Guid TypeA = Guid.NewGuid();
        public Guid TypeB = Guid.NewGuid();
        public int CallCount;

        // Mutable breed store so Create/Delete mutations are visible to subsequent
        // reads, mirroring how the real ConfigurationService behaves.
        public List<BreedDto> Breeds { get; } = new();

        public Task<Result<List<BreedDto>>> GetBreedsAsync(Guid farmId, Guid? animalTypeId = null)
        {
            CallCount++;

            List<BreedDto> breeds;
            if (Breeds.Count > 0)
            {
                breeds = animalTypeId.HasValue
                    ? Breeds.Where(b => b.AnimalTypeId == animalTypeId.Value).ToList()
                    : Breeds.ToList();
            }
            else
            {
                // Legacy default used by the cross-type isolation test: one breed per type.
                var name = animalTypeId == TypeA ? "Breed-A" : "Breed-B";
                breeds = new List<BreedDto>
                {
                    new BreedDto { Id = Guid.NewGuid(), Name = name, AnimalTypeId = animalTypeId ?? Guid.Empty }
                };
            }

            return Task.FromResult(Result<List<BreedDto>>.Success(breeds));
        }

        public Task<Result<BreedDto>> CreateBreedAsync(Guid farmId, CreateBreedRequest r)
        {
            var breed = new BreedDto
            {
                Id = Guid.NewGuid(),
                Name = r.Name,
                AnimalTypeId = r.AnimalTypeId,
                AverageGestationDays = r.AverageGestationDays
            };
            Breeds.Add(breed);
            return Task.FromResult(Result<BreedDto>.Success(breed));
        }

        public Task<Result> DeleteBreedAsync(Guid farmId, Guid id)
        {
            Breeds.RemoveAll(b => b.Id == id);
            return Task.FromResult(Result.Success());
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
        Assert.Equal("Breed-B", typeB.Value![0].Name);
    }

    [Fact]
    public async Task CreateBreed_InvalidatesCachedBreedLists_Immediately()
    {
        var inner = new FakeInner();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var svc = new CachedConfigurationService(inner, cache);
        var farmId = Guid.NewGuid();

        // Seed the inner store so reads are deterministic (bypasses the legacy one-breed default).
        inner.Breeds.Add(new BreedDto { Id = Guid.NewGuid(), Name = "Holstein", AnimalTypeId = inner.TypeA });

        // Warm two distinct cached breed keys: scoped to TypeA, and unscoped.
        var scopedBefore = await svc.GetBreedsAsync(farmId, inner.TypeA);
        Assert.True(scopedBefore.IsSuccess);
        Assert.Equal("Holstein", scopedBefore.Value![0].Name);

        var unscopedBefore = await svc.GetBreedsAsync(farmId, null);
        Assert.True(unscopedBefore.IsSuccess);
        Assert.Single(unscopedBefore.Value!);

        var callsAfterWarm = inner.CallCount;

        // Add a breed for TypeA through the caching service.
        var created = await svc.CreateBreedAsync(farmId, new CreateBreedRequest { Name = "Jersey", AnimalTypeId = inner.TypeA });
        Assert.True(created.IsSuccess);

        // Both previously cached lists must reflect the new breed on the very next
        // read — not after the ~5 minute TTL. (Pre-fix: invalidation misses the
        // scoped keys entirely, so these reads return the stale 1-item cache.)
        var scopedAfter = await svc.GetBreedsAsync(farmId, inner.TypeA);
        Assert.True(scopedAfter.IsSuccess);
        Assert.Equal(2, scopedAfter.Value!.Count);
        Assert.Contains(scopedAfter.Value!, b => b.Name == "Jersey");

        var unscopedAfter = await svc.GetBreedsAsync(farmId, null);
        Assert.True(unscopedAfter.IsSuccess);
        Assert.Equal(2, unscopedAfter.Value!.Count);
        Assert.Contains(unscopedAfter.Value!, b => b.Name == "Jersey");

        // Prove the data came from the inner service, not a surviving cache entry.
        Assert.True(inner.CallCount > callsAfterWarm, "reads must bypass invalidated cache entries");
    }
}
