using FMS.Application.Breeding;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/breeding-records")]
[Authorize]
public class BreedingRecordsController : ControllerBase
{
    private readonly IBreedingService _breedingService;

    public BreedingRecordsController(IBreedingService breedingService)
    {
        _breedingService = breedingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBreedingRecords(Guid farmId, [FromQuery] BreedingRecordListFilter filter)
    {
        var result = await _breedingService.GetBreedingRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBreedingRecord(Guid farmId, Guid id)
    {
        var result = await _breedingService.GetBreedingRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> CreateBreedingRecord(Guid farmId, [FromBody] CreateBreedingRecordRequest request)
    {
        var result = await _breedingService.CreateBreedingRecordAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBreedingRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> UpdateBreedingRecord(Guid farmId, Guid id, [FromBody] UpdateBreedingRecordRequest request)
    {
        var result = await _breedingService.UpdateBreedingRecordAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> DeleteBreedingRecord(Guid farmId, Guid id)
    {
        var result = await _breedingService.DeleteBreedingRecordAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
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
