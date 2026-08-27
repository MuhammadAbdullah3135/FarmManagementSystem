using FMS.Application.Common;

namespace FMS.Application.Configuration;

public interface IConfigurationService
{
    // Animal Types
    Task<Result<List<AnimalTypeDto>>> GetAnimalTypesAsync(Guid farmId);
    Task<Result<AnimalTypeDto>> CreateAnimalTypeAsync(Guid farmId, CreateAnimalTypeRequest request);
    Task<Result> DeleteAnimalTypeAsync(Guid farmId, Guid id);

    // Breeds
    Task<Result<List<BreedDto>>> GetBreedsAsync(Guid farmId, Guid? animalTypeId = null);
    Task<Result<BreedDto>> CreateBreedAsync(Guid farmId, CreateBreedRequest request);
    Task<Result> DeleteBreedAsync(Guid farmId, Guid id);

    // Sex Options
    Task<Result<List<SexOptionDto>>> GetSexOptionsAsync(Guid farmId);
    Task<Result<SexOptionDto>> CreateSexOptionAsync(Guid farmId, CreateSexOptionRequest request);
    Task<Result> DeleteSexOptionAsync(Guid farmId, Guid id);

    // Age Categories
    Task<Result<List<AgeCategoryDto>>> GetAgeCategoriesAsync(Guid farmId);
    Task<Result<AgeCategoryDto>> CreateAgeCategoryAsync(Guid farmId, CreateAgeCategoryRequest request);
    Task<Result> DeleteAgeCategoryAsync(Guid farmId, Guid id);

    // Animal Statuses
    Task<Result<List<AnimalStatusDto>>> GetAnimalStatusesAsync(Guid farmId);
    Task<Result<AnimalStatusDto>> CreateAnimalStatusAsync(Guid farmId, CreateAnimalStatusRequest request);
    Task<Result> DeleteAnimalStatusAsync(Guid farmId, Guid id);

    // Identification Types
    Task<Result<List<IdentificationTypeDto>>> GetIdentificationTypesAsync(Guid farmId);
    Task<Result<IdentificationTypeDto>> CreateIdentificationTypeAsync(Guid farmId, CreateIdentificationTypeRequest request);
    Task<Result> DeleteIdentificationTypeAsync(Guid farmId, Guid id);

    // Location Types
    Task<Result<List<LocationTypeDto>>> GetLocationTypesAsync(Guid farmId);
    Task<Result<LocationTypeDto>> CreateLocationTypeAsync(Guid farmId, CreateLocationTypeRequest request);
    Task<Result> DeleteLocationTypeAsync(Guid farmId, Guid id);

    // Locations
    Task<Result<List<LocationDto>>> GetLocationsAsync(Guid farmId);
    Task<Result<LocationDto>> CreateLocationAsync(Guid farmId, CreateLocationRequest request);
    Task<Result> DeleteLocationAsync(Guid farmId, Guid id);

    // Custom Fields
    Task<Result<List<CustomFieldDefinitionDto>>> GetCustomFieldDefinitionsAsync(Guid farmId);
    Task<Result<CustomFieldDefinitionDto>> CreateCustomFieldDefinitionAsync(Guid farmId, CreateCustomFieldDefinitionRequest request);
    Task<Result> DeleteCustomFieldDefinitionAsync(Guid farmId, Guid id);

    // Farm Configuration (key-value)
    Task<Result<List<FarmConfigurationDto>>> GetFarmConfigurationsAsync(Guid farmId);
    Task<Result<FarmConfigurationDto>> UpsertFarmConfigurationAsync(Guid farmId, UpdateFarmConfigurationRequest request);
    Task<Result> DeleteFarmConfigurationAsync(Guid farmId, string key);
}
