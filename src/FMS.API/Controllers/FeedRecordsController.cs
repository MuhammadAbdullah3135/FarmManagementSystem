using FMS.Application.Common;
using FMS.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/feed/records")]
[Authorize]
public class FeedRecordsController : ControllerBase
{
    private readonly IFeedService _feedService;

    public FeedRecordsController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpGet]
    public async Task<IActionResult> GetFeedRecords(Guid farmId, [FromQuery] FeedRecordListFilter filter)
    {
        var result = await _feedService.GetFeedRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFeedRecord(Guid farmId, Guid id)
    {
        var result = await _feedService.GetFeedRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateFeedRecord(Guid farmId, [FromBody] CreateFeedRecordRequest request)
    {
        var result = await _feedService.CreateFeedRecordAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetFeedRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateFeedRecord(Guid farmId, Guid id, [FromBody] UpdateFeedRecordRequest request)
    {
        var result = await _feedService.UpdateFeedRecordAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteFeedRecord(Guid farmId, Guid id)
    {
        var result = await _feedService.DeleteFeedRecordAsync(farmId, id);
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
