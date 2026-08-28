using FMS.Application.Common;
using FMS.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(Guid farmId)
    {
        var result = await _dashboardService.GetSummaryAsync(farmId);
        return MapResult(result);
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(Guid farmId)
    {
        var result = await _dashboardService.GetAlertsAsync(farmId);
        return MapResult(result);
    }

    [HttpGet("charts")]
    public async Task<IActionResult> GetCharts(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _dashboardService.GetChartsAsync(farmId, from, to);
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
