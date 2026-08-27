using FMS.Application.Breeding;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/births")]
[Authorize]
public class BirthsController : ControllerBase
{
    private readonly IBreedingService _breedingService;

    public BirthsController(IBreedingService breedingService)
    {
        _breedingService = breedingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBirthRecords(Guid farmId, [FromQuery] BirthRecordListFilter filter)
    {
        var result = await _breedingService.GetBirthRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBirthRecord(Guid farmId, Guid id)
    {
        var result = await _breedingService.GetBirthRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> CreateBirthRecord(Guid farmId, [FromBody] CreateBirthRecordRequest request)
    {
        var result = await _breedingService.CreateBirthRecordAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBirthRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> DeleteBirthRecord(Guid farmId, Guid id)
    {
        var result = await _breedingService.DeleteBirthRecordAsync(farmId, id);
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
