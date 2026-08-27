using FMS.Application.Common;
using FMS.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/health/costs")]
[Authorize]
public class HealthCostsController : ControllerBase
{
    private readonly IHealthCostService _healthCostService;

    public HealthCostsController(IHealthCostService healthCostService)
    {
        _healthCostService = healthCostService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetCostSummary(Guid farmId, [FromQuery] HealthCostFilter filter)
    {
        var result = await _healthCostService.GetCostSummaryAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("by-vet")]
    public async Task<IActionResult> GetCostsByVet(Guid farmId, [FromQuery] HealthCostFilter filter)
    {
        var result = await _healthCostService.GetCostsByVetAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("by-animal")]
    public async Task<IActionResult> GetCostsByAnimal(Guid farmId, [FromQuery] HealthCostFilter filter)
    {
        var result = await _healthCostService.GetCostsByAnimalAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("by-month")]
    public async Task<IActionResult> GetCostsByMonth(Guid farmId, [FromQuery] int year)
    {
        if (year < 2000 || year > 2100) year = DateTime.UtcNow.Year;
        var result = await _healthCostService.GetCostsByMonthAsync(farmId, year);
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
