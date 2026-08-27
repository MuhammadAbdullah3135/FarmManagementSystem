using FMS.Application.Breeding;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/breeding/reports")]
[Authorize]
public class BreedingReportsController : ControllerBase
{
    private readonly IBreedingService _breedingService;

    public BreedingReportsController(IBreedingService breedingService)
    {
        _breedingService = breedingService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(Guid farmId, [FromQuery] BreedingReportFilter filter)
    {
        var result = await _breedingService.GetBreedingSummaryAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("trend")]
    public async Task<IActionResult> GetTrend(Guid farmId, [FromQuery] BreedingReportFilter filter)
    {
        var result = await _breedingService.GetBreedingTrendAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("methods")]
    public async Task<IActionResult> GetMethodDistribution(Guid farmId, [FromQuery] BreedingReportFilter filter)
    {
        var result = await _breedingService.GetMethodDistributionAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("sires")]
    public async Task<IActionResult> GetSirePerformance(Guid farmId, [FromQuery] BreedingReportFilter filter)
    {
        var result = await _breedingService.GetSirePerformanceAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("calendar")]
    public async Task<IActionResult> GetCalendarEvents(Guid farmId, [FromQuery] int daysAhead = 30)
    {
        daysAhead = Math.Clamp(daysAhead, 1, 365);
        var result = await _breedingService.GetUpcomingCalendarEventsAsync(farmId, daysAhead);
        return MapResult(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
