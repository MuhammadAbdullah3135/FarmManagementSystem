using FMS.Application.Attendance;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendanceService;

    public AttendanceController(IAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAttendance(Guid farmId, [FromQuery] AttendanceListFilter filter)
    {
        var result = await _attendanceService.GetAttendanceAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpPost("{employeeId:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid farmId, Guid employeeId)
    {
        var result = await _attendanceService.CheckInAsync(farmId, employeeId);
        return MapResult(result);
    }

    [HttpPost("{employeeId:guid}/check-out")]
    public async Task<IActionResult> CheckOut(Guid farmId, Guid employeeId)
    {
        var result = await _attendanceService.CheckOutAsync(farmId, employeeId);
        return MapResult(result);
    }

    [HttpPut]
    public async Task<IActionResult> UpsertAttendance(Guid farmId, [FromBody] UpsertAttendanceRequest request)
    {
        var result = await _attendanceService.UpsertAttendanceAsync(farmId, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAttendance(Guid farmId, Guid id)
    {
        var result = await _attendanceService.DeleteAttendanceAsync(farmId, id);
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
