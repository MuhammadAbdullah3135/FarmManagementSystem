using FMS.Application.Common;
using FMS.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/weight-schedules")]
[Authorize]
public class WeightCheckSchedulesController : ControllerBase
{
    private readonly IWeightCheckScheduleService _service;

    public WeightCheckSchedulesController(IWeightCheckScheduleService service)
    {
        _service = service;
    }

    // ── CRUD (read: all roles, write: Vet/FarmManager/SystemOwner/Owner) ──

    [HttpGet]
    public async Task<IActionResult> GetSchedules(Guid farmId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetSchedulesAsync(farmId, page, pageSize);
        return MapResult(result);
    }

    [HttpPost]
    [Authorize(Roles = "Veterinarian,FarmManager,SystemOwner")]
    public async Task<IActionResult> CreateSchedule(Guid farmId, [FromBody] CreateWeightCheckScheduleRequest request)
    {
        var result = await _service.CreateScheduleAsync(farmId, request);
        return result.IsSuccess
            ? Ok(result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Veterinarian,FarmManager,SystemOwner")]
    public async Task<IActionResult> UpdateSchedule(Guid farmId, Guid id, [FromBody] UpdateWeightCheckScheduleRequest request)
    {
        var result = await _service.UpdateScheduleAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Veterinarian,FarmManager,SystemOwner")]
    public async Task<IActionResult> DeleteSchedule(Guid farmId, Guid id)
    {
        var result = await _service.DeleteScheduleAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // ── Status ───────────────────────────────────────────

    [HttpGet("status")]
    public async Task<IActionResult> GetWeightCheckStatus(Guid farmId)
    {
        var result = await _service.GetWeightCheckStatusAsync(farmId);
        return MapResult(result);
    }

    [HttpGet("status/overdue")]
    public async Task<IActionResult> GetOverdueWeightChecks(Guid farmId)
    {
        var result = await _service.GetOverdueWeightChecksAsync(farmId);
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
