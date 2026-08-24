using FMS.Application.Animal;
using FMS.Application.Common;
using FMS.Application.Farm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/animals")]
[Authorize]
public class AnimalsController : ControllerBase
{
    private readonly IAnimalService _animalService;
    private readonly IFarmContextService _farmContext;

    public AnimalsController(IAnimalService animalService, IFarmContextService farmContext)
    {
        _animalService = animalService;
        _farmContext = farmContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAnimals(Guid farmId, [FromQuery] AnimalListFilter filter)
    {
        var result = await _animalService.GetAnimalsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetAnimal(Guid farmId, Guid id)
    {
        var result = await _animalService.GetAnimalByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAnimal(Guid farmId, [FromBody] CreateAnimalRequest request)
    {
        var result = await _animalService.CreateAnimalAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetAnimal), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAnimal(Guid farmId, Guid id, [FromBody] UpdateAnimalRequest request)
    {
        var result = await _animalService.UpdateAnimalAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAnimal(Guid farmId, Guid id)
    {
        var result = await _animalService.DeleteAnimalAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid farmId, Guid id, [FromBody] ChangeAnimalStatusRequest request)
    {
        var result = await _animalService.ChangeStatusAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/identifications")]
    public async Task<IActionResult> AddIdentification(Guid farmId, Guid id, [FromBody] AddAnimalIdentificationRequest request)
    {
        var result = await _animalService.AddIdentificationAsync(farmId, id, request);
        return result.IsSuccess ? Created("", result.Value) : MapResult(result);
    }

    [HttpDelete("{id:guid}/identifications/{identificationId:guid}")]
    public async Task<IActionResult> RemoveIdentification(Guid farmId, Guid id, Guid identificationId)
    {
        var result = await _animalService.RemoveIdentificationAsync(farmId, id, identificationId);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpGet("{id:guid}/weights")]
    public async Task<IActionResult> GetWeights(Guid farmId, Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _animalService.GetWeightsAsync(farmId, id, page, pageSize);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/weights")]
    public async Task<IActionResult> AddWeight(Guid farmId, Guid id, [FromBody] CreateWeightRecordRequest request)
    {
        var result = await _animalService.AddWeightAsync(farmId, id, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetWeights), new { farmId, id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}/weights/{weightId:guid}")]
    public async Task<IActionResult> UpdateWeight(Guid farmId, Guid id, Guid weightId, [FromBody] UpdateWeightRecordRequest request)
    {
        var result = await _animalService.UpdateWeightAsync(farmId, id, weightId, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}/weights/{weightId:guid}")]
    public async Task<IActionResult> DeleteWeight(Guid farmId, Guid id, Guid weightId)
    {
        var result = await _animalService.DeleteWeightAsync(farmId, id, weightId);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpGet("{id:guid}/weights/report")]
    public async Task<IActionResult> GetWeightReport(Guid farmId, Guid id)
    {
        var result = await _animalService.GetWeightReportAsync(farmId, id);
        return MapResult(result);
    }

    [HttpGet("{id:guid}/timeline")]
    public async Task<IActionResult> GetTimeline(Guid farmId, Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? eventType = null)
    {
        var result = await _animalService.GetTimelineAsync(farmId, id, page, pageSize, eventType);
        return MapResult(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
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
