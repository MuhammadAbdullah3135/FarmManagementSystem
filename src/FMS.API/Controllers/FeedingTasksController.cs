using FMS.Application.Common;
using FMS.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/feed/tasks")]
[Authorize]
public class FeedingTasksController : ControllerBase
{
    private readonly IFeedService _feedService;

    public FeedingTasksController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateTasks(Guid farmId, [FromBody] GenerateFeedingTasksRequest request)
    {
        var result = await _feedService.GenerateTasksAsync(farmId, request);
        return result.IsSuccess ? Ok(result.Value) : MapResult(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetTasks(Guid farmId, [FromQuery] FeedingTaskListFilter filter)
    {
        var result = await _feedService.GetTasksAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteTask(Guid farmId, Guid id, [FromBody] CompleteFeedingTaskRequest? request)
    {
        var result = await _feedService.CompleteTaskAsync(farmId, id, request ?? new CompleteFeedingTaskRequest());
        return MapResult(result);
    }

    [HttpPost("{id:guid}/skip")]
    public async Task<IActionResult> SkipTask(Guid farmId, Guid id, [FromBody] CompleteFeedingTaskRequest? request)
    {
        var result = await _feedService.SkipTaskAsync(farmId, id, request ?? new CompleteFeedingTaskRequest());
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
