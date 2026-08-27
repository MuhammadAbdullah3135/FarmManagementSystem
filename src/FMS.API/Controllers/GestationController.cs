using FMS.Application.Breeding;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/gestation")]
[Authorize]
public class GestationController : ControllerBase
{
    private readonly IBreedingService _breedingService;

    public GestationController(IBreedingService breedingService)
    {
        _breedingService = breedingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetGestationRecords(Guid farmId, [FromQuery] GestationRecordListFilter filter)
    {
        var result = await _breedingService.GetGestationRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetGestationRecord(Guid farmId, Guid id)
    {
        var result = await _breedingService.GetGestationRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost("confirm")]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> ConfirmPregnancy(Guid farmId, [FromBody] ConfirmPregnancyRequest request)
    {
        var result = await _breedingService.ConfirmPregnancyAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetGestationRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPost("{id:guid}/revert")]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> RevertPregnancy(Guid farmId, Guid id, [FromBody] RevertPregnancyRequest request)
    {
        var result = await _breedingService.RevertPregnancyAsync(farmId, id, request.Reason);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpGet("{id:guid}/health-checks")]
    public async Task<IActionResult> GetHealthChecks(Guid farmId, Guid id)
    {
        var result = await _breedingService.GetHealthChecksAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/health-checks")]
    [Authorize(Roles = "Vet,FarmManager,SystemOwner,Owner")]
    public async Task<IActionResult> LogHealthCheck(Guid farmId, Guid id, [FromBody] LogGestationHealthCheckRequest request)
    {
        var result = await _breedingService.LogHealthCheckAsync(farmId, id, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetHealthChecks), new { farmId, id }, result.Value)
            : MapResult(result);
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

public class RevertPregnancyRequest
{
    public string Reason { get; set; } = string.Empty;
}
