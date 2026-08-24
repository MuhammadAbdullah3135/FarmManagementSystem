using FMS.Application.Common;
using FMS.Application.Performance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/performance-reviews")]
[Authorize]
public class PerformanceReviewsController : ControllerBase
{
    private readonly IPerformanceService _performanceService;

    public PerformanceReviewsController(IPerformanceService performanceService)
    {
        _performanceService = performanceService;
    }

    [HttpGet]
    public async Task<IActionResult> GetReviews(Guid farmId, [FromQuery] PerformanceReviewListFilter filter)
    {
        var result = await _performanceService.GetReviewsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetReview(Guid farmId, Guid id)
    {
        var result = await _performanceService.GetReviewByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateReview(Guid farmId, [FromBody] CreatePerformanceReviewRequest request)
    {
        var result = await _performanceService.CreateReviewAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetReview), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateReview(Guid farmId, Guid id, [FromBody] UpdatePerformanceReviewRequest request)
    {
        var result = await _performanceService.UpdateReviewAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteReview(Guid farmId, Guid id)
    {
        var result = await _performanceService.DeleteReviewAsync(farmId, id);
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
