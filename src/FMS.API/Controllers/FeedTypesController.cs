using FMS.Application.Common;
using FMS.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/feed/types")]
[Authorize]
public class FeedTypesController : ControllerBase
{
    private readonly IFeedService _feedService;

    public FeedTypesController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpGet]
    public async Task<IActionResult> GetFeedTypes(Guid farmId)
    {
        var result = await _feedService.GetFeedTypesAsync(farmId);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFeedType(Guid farmId, Guid id)
    {
        var result = await _feedService.GetFeedTypeByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateFeedType(Guid farmId, [FromBody] CreateFeedTypeRequest request)
    {
        var result = await _feedService.CreateFeedTypeAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetFeedType), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateFeedType(Guid farmId, Guid id, [FromBody] UpdateFeedTypeRequest request)
    {
        var result = await _feedService.UpdateFeedTypeAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFeedType(Guid farmId, Guid id)
    {
        var result = await _feedService.DeleteFeedTypeAsync(farmId, id);
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
