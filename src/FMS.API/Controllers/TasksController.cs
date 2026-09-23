using FMS.Application.Common;
using FMS.Application.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/tasks")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly IFarmTaskService _taskService;

    public TasksController(IFarmTaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet]
    public async Task<IActionResult> GetTasks(Guid farmId, [FromQuery] FarmTaskListFilter filter)
    {
        var result = await _taskService.GetTasksAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTask(Guid farmId, Guid id)
    {
        var result = await _taskService.GetTaskByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask(Guid farmId, [FromBody] CreateFarmTaskRequest request)
    {
        var result = await _taskService.CreateFarmTaskAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetTask), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTask(Guid farmId, Guid id, [FromBody] UpdateFarmTaskRequest request)
    {
        var result = await _taskService.UpdateFarmTaskAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteTask(Guid farmId, Guid id)
    {
        var result = await _taskService.DeleteFarmTaskAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> StartTask(Guid farmId, Guid id)
    {
        var result = await _taskService.StartTaskAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> CompleteTask(Guid farmId, Guid id, [FromBody] CompleteFarmTaskRequest request)
    {
        var result = await _taskService.CompleteTaskAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelTask(Guid farmId, Guid id, [FromBody] CancelFarmTaskRequest request)
    {
        var result = await _taskService.CancelTaskAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> ReopenTask(Guid farmId, Guid id)
    {
        var result = await _taskService.ReopenTaskAsync(farmId, id);
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
        // A completion that is already satisfied answers 409 as it always has (nothing was
        // written). The separate code is what lets the sync endpoint report it as done to a
        // device instead of as a refusal, without either side matching on message text.
        Error.SupersededCode => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
