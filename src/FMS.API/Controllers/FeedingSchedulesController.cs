using FMS.Application.Common;
using FMS.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/feed/schedules")]
[Authorize]
public class FeedingSchedulesController : ControllerBase
{
    private readonly IFeedService _feedService;

    public FeedingSchedulesController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSchedules(Guid farmId, [FromQuery] Guid? dietPlanId)
    {
        var result = await _feedService.GetSchedulesAsync(farmId, dietPlanId);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSchedule(Guid farmId, [FromBody] CreateFeedingScheduleRequest request)
    {
        var result = await _feedService.CreateScheduleAsync(farmId, request);
        return result.IsSuccess ? Created("", result.Value) : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateSchedule(Guid farmId, Guid id, [FromBody] UpdateFeedingScheduleRequest request)
    {
        var result = await _feedService.UpdateScheduleAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSchedule(Guid farmId, Guid id)
    {
        var result = await _feedService.DeleteScheduleAsync(farmId, id);
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
