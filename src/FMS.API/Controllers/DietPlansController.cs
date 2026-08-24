using FMS.Application.Common;
using FMS.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/feed/diet-plans")]
[Authorize]
public class DietPlansController : ControllerBase
{
    private readonly IFeedService _feedService;

    public DietPlansController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDietPlans(Guid farmId)
    {
        var result = await _feedService.GetDietPlansAsync(farmId);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDietPlan(Guid farmId, Guid id)
    {
        var result = await _feedService.GetDietPlanByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateDietPlan(Guid farmId, [FromBody] CreateDietPlanRequest request)
    {
        var result = await _feedService.CreateDietPlanAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetDietPlan), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateDietPlan(Guid farmId, Guid id, [FromBody] UpdateDietPlanRequest request)
    {
        var result = await _feedService.UpdateDietPlanAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDietPlan(Guid farmId, Guid id)
    {
        var result = await _feedService.DeleteDietPlanAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItem(Guid farmId, Guid id, [FromBody] AddDietPlanItemRequest request)
    {
        var result = await _feedService.AddDietPlanItemAsync(farmId, id, request);
        return result.IsSuccess ? Ok(result.Value) : MapResult(result);
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid farmId, Guid id, Guid itemId)
    {
        var result = await _feedService.RemoveDietPlanItemAsync(farmId, id, itemId);
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
