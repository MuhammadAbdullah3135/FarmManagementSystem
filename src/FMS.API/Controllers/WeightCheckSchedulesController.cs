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

    /// <summary>
    /// The weight-check status projection for the farm.
    ///
    /// <para>
    /// The response is the same envelope the other cached collections use (items, a cursor, and
    /// the ids of rows deleted since) rather than the bare array it used to be: it is one of
    /// the three lists a device caches, and it cannot be deltaed without a server-owned cursor
    /// for the client to send back. Callers that want rows read <c>items</c>.
    /// </para>
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetWeightCheckStatus(Guid farmId, [FromQuery] DateTime? updatedSince = null)
    {
        var result = await _service.GetWeightCheckStatusAsync(farmId, updatedSince);
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
