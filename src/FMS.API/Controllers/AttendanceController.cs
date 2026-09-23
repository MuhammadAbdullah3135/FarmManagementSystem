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

    /// <summary>
    /// The body is optional. A live check-in sends none and the server stamps the time; a
    /// device that was offline supplies the time it was actually at the gate.
    /// </summary>
    [HttpPost("{employeeId:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid farmId, Guid employeeId, [FromBody] CheckInRequest? request = null)
    {
        var result = await _attendanceService.CheckInAsync(farmId, employeeId, request);
        return MapResult(result);
    }

    [HttpPost("{employeeId:guid}/check-out")]
    public async Task<IActionResult> CheckOut(Guid farmId, Guid employeeId, [FromBody] CheckOutRequest? request = null)
    {
        var result = await _attendanceService.CheckOutAsync(farmId, employeeId, request);
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
        // "Your intent is already satisfied" is a conflict over HTTP too — nothing was written —
        // and it has always answered 409 here. The distinct code exists for the queued path,
        // where the caller has to tell it apart from a refusal without reading message text.
        Error.SupersededCode => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
