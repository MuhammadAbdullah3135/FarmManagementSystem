using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Application.Farm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/[controller]")]
[Authorize]
public class ConfigurationController : ControllerBase
{
    private readonly IConfigurationService _configurationService;
    private readonly IFarmContextService _farmContext;

    public ConfigurationController(
        IConfigurationService configurationService,
        IFarmContextService farmContext)
    {
        _configurationService = configurationService;
        _farmContext = farmContext;
    }

    private Guid GetFarmId() => _farmContext.GetCurrentFarmId() ?? Guid.Empty;

    // Animal Types
    [HttpGet("animal-types")]
    public async Task<IActionResult> GetAnimalTypes(Guid farmId)
    {
        var result = await _configurationService.GetAnimalTypesAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("animal-types")]
    public async Task<IActionResult> CreateAnimalType(Guid farmId, [FromBody] CreateAnimalTypeRequest request)
    {
        var result = await _configurationService.CreateAnimalTypeAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("animal-types/{id:guid}")]
    public async Task<IActionResult> DeleteAnimalType(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteAnimalTypeAsync(farmId, id);
        return MapDeleted(result);
    }

    // Breeds
    [HttpGet("breeds")]
    public async Task<IActionResult> GetBreeds(Guid farmId, [FromQuery] Guid? animalTypeId = null)
    {
        var result = await _configurationService.GetBreedsAsync(farmId, animalTypeId);
        return MapResult(result);
    }

    [HttpPost("breeds")]
    public async Task<IActionResult> CreateBreed(Guid farmId, [FromBody] CreateBreedRequest request)
    {
        var result = await _configurationService.CreateBreedAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("breeds/{id:guid}")]
    public async Task<IActionResult> DeleteBreed(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteBreedAsync(farmId, id);
        return MapDeleted(result);
    }

    // Sex Options
    [HttpGet("sex-options")]
    public async Task<IActionResult> GetSexOptions(Guid farmId)
    {
        var result = await _configurationService.GetSexOptionsAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("sex-options")]
    public async Task<IActionResult> CreateSexOption(Guid farmId, [FromBody] CreateSexOptionRequest request)
    {
        var result = await _configurationService.CreateSexOptionAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("sex-options/{id:guid}")]
    public async Task<IActionResult> DeleteSexOption(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteSexOptionAsync(farmId, id);
        return MapDeleted(result);
    }

    // Age Categories
    [HttpGet("age-categories")]
    public async Task<IActionResult> GetAgeCategories(Guid farmId)
    {
        var result = await _configurationService.GetAgeCategoriesAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("age-categories")]
    public async Task<IActionResult> CreateAgeCategory(Guid farmId, [FromBody] CreateAgeCategoryRequest request)
    {
        var result = await _configurationService.CreateAgeCategoryAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("age-categories/{id:guid}")]
    public async Task<IActionResult> DeleteAgeCategory(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteAgeCategoryAsync(farmId, id);
        return MapDeleted(result);
    }

    // Animal Statuses
    [HttpGet("statuses")]
    public async Task<IActionResult> GetAnimalStatuses(Guid farmId)
    {
        var result = await _configurationService.GetAnimalStatusesAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("statuses")]
    public async Task<IActionResult> CreateAnimalStatus(Guid farmId, [FromBody] CreateAnimalStatusRequest request)
    {
        var result = await _configurationService.CreateAnimalStatusAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("statuses/{id:guid}")]
    public async Task<IActionResult> DeleteAnimalStatus(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteAnimalStatusAsync(farmId, id);
        return MapDeleted(result);
    }

    // Identification Types
    [HttpGet("identification-types")]
    public async Task<IActionResult> GetIdentificationTypes(Guid farmId)
    {
        var result = await _configurationService.GetIdentificationTypesAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("identification-types")]
    public async Task<IActionResult> CreateIdentificationType(Guid farmId, [FromBody] CreateIdentificationTypeRequest request)
    {
        var result = await _configurationService.CreateIdentificationTypeAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("identification-types/{id:guid}")]
    public async Task<IActionResult> DeleteIdentificationType(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteIdentificationTypeAsync(farmId, id);
        return MapDeleted(result);
    }

    // Location Types
    [HttpGet("location-types")]
    public async Task<IActionResult> GetLocationTypes(Guid farmId)
    {
        var result = await _configurationService.GetLocationTypesAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("location-types")]
    public async Task<IActionResult> CreateLocationType(Guid farmId, [FromBody] CreateLocationTypeRequest request)
    {
        var result = await _configurationService.CreateLocationTypeAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("location-types/{id:guid}")]
    public async Task<IActionResult> DeleteLocationType(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteLocationTypeAsync(farmId, id);
        return MapDeleted(result);
    }

    // Locations
    [HttpGet("locations")]
    public async Task<IActionResult> GetLocations(Guid farmId)
    {
        var result = await _configurationService.GetLocationsAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("locations")]
    public async Task<IActionResult> CreateLocation(Guid farmId, [FromBody] CreateLocationRequest request)
    {
        var result = await _configurationService.CreateLocationAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("locations/{id:guid}")]
    public async Task<IActionResult> DeleteLocation(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteLocationAsync(farmId, id);
        return MapDeleted(result);
    }

    // Custom Fields
    [HttpGet("custom-fields")]
    public async Task<IActionResult> GetCustomFieldDefinitions(Guid farmId)
    {
        var result = await _configurationService.GetCustomFieldDefinitionsAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("custom-fields")]
    public async Task<IActionResult> CreateCustomFieldDefinition(Guid farmId, [FromBody] CreateCustomFieldDefinitionRequest request)
    {
        var result = await _configurationService.CreateCustomFieldDefinitionAsync(farmId, request);
        return MapCreated(result);
    }

    [HttpDelete("custom-fields/{id:guid}")]
    public async Task<IActionResult> DeleteCustomFieldDefinition(Guid farmId, Guid id)
    {
        var result = await _configurationService.DeleteCustomFieldDefinitionAsync(farmId, id);
        return MapDeleted(result);
    }

    // Farm Configurations
    [HttpGet("farm-config")]
    public async Task<IActionResult> GetFarmConfigurations(Guid farmId)
    {
        var result = await _configurationService.GetFarmConfigurationsAsync(farmId);
        return MapResult(result);
    }

    [HttpPut("farm-config")]
    public async Task<IActionResult> UpsertFarmConfiguration(Guid farmId, [FromBody] UpdateFarmConfigurationRequest request)
    {
        var result = await _configurationService.UpsertFarmConfigurationAsync(farmId, request);
        return MapResult(result);
    }

    [HttpDelete("farm-config/{key}")]
    public async Task<IActionResult> DeleteFarmConfiguration(Guid farmId, string key)
    {
        var result = await _configurationService.DeleteFarmConfigurationAsync(farmId, key);
        return MapDeleted(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    private IActionResult MapCreated<T>(Result<T> result) => result.IsSuccess
        ? Created("", result.Value)
        : MapError(result.Error!);

    /// <summary>Deletes answer 204 with no body, so only the failure path needs mapping.</summary>
    private IActionResult MapDeleted(Result result) => result.IsSuccess
        ? NoContent()
        : MapError(result.Error!);

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        "Conflict" => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
