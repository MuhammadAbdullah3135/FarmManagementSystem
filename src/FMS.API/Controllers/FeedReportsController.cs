using FMS.Application.Common;
using FMS.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/feed/reports")]
[Authorize]
public class FeedReportsController : ControllerBase
{
    private readonly IFeedService _feedService;

    public FeedReportsController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpGet("consumption/trend")]
    public async Task<IActionResult> GetConsumptionTrend(
        Guid farmId,
        [FromQuery] string period = "day",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _feedService.GetConsumptionTrendAsync(farmId, period, from, to);
        return MapResult(result);
    }

    [HttpGet("consumption/by-feed-type")]
    public async Task<IActionResult> GetConsumptionByFeedType(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _feedService.GetConsumptionByFeedTypeAsync(farmId, from, to);
        return MapResult(result);
    }

    [HttpGet("consumption/by-animal")]
    public async Task<IActionResult> GetConsumptionByAnimal(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _feedService.GetConsumptionByAnimalAsync(farmId, from, to);
        return MapResult(result);
    }

    [HttpGet("consumption/by-location")]
    public async Task<IActionResult> GetConsumptionByLocation(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _feedService.GetConsumptionByLocationAsync(farmId, from, to);
        return MapResult(result);
    }

    [HttpGet("cost-summary")]
    public async Task<IActionResult> GetCostSummary(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _feedService.GetCostSummaryAsync(farmId, from, to);
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
