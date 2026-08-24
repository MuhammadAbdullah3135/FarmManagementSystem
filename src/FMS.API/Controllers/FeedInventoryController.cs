using FMS.Application.Common;
using FMS.Application.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/feed/inventory")]
[Authorize]
public class FeedInventoryController : ControllerBase
{
    private readonly IFeedService _feedService;

    public FeedInventoryController(IFeedService feedService)
    {
        _feedService = feedService;
    }

    [HttpPost("movements")]
    public async Task<IActionResult> RecordMovement(Guid farmId, [FromBody] RecordStockMovementRequest request)
    {
        var result = await _feedService.RecordStockMovementAsync(farmId, request);
        return result.IsSuccess ? Created("", result.Value) : MapResult(result);
    }

    [HttpGet("movements")]
    public async Task<IActionResult> GetMovements(
        Guid farmId,
        [FromQuery] Guid? feedTypeId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _feedService.GetStockMovementsAsync(farmId, feedTypeId, page, pageSize);
        return MapResult(result);
    }

    [HttpGet("stock")]
    public async Task<IActionResult> GetStock(Guid farmId)
    {
        var result = await _feedService.GetStockAsync(farmId);
        return MapResult(result);
    }

    [HttpGet("stock/{feedTypeId:guid}")]
    public async Task<IActionResult> GetStockByFeedType(Guid farmId, Guid feedTypeId)
    {
        var result = await _feedService.GetStockByFeedTypeAsync(farmId, feedTypeId);
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
