using FMS.Application.Common;
using FMS.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly ICostAttributionService _costAttribution;

    public ReportsController(IReportService reportService, ICostAttributionService costAttribution)
    {
        _reportService = reportService;
        _costAttribution = costAttribution;
    }

    [HttpGet("animals")]
    public async Task<IActionResult> GetAnimalReport(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _reportService.GetAnimalReportAsync(farmId, from, to);
        return MapResult(result);
    }

    [HttpGet("medical")]
    public async Task<IActionResult> GetMedicalReport(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _reportService.GetMedicalReportAsync(farmId, from, to);
        return MapResult(result);
    }

    [HttpGet("vaccination")]
    public async Task<IActionResult> GetVaccinationReport(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _reportService.GetVaccinationReportAsync(farmId, from, to);
        return MapResult(result);
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployeeReport(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _reportService.GetEmployeeReportAsync(farmId, from, to);
        return MapResult(result);
    }

    /// <summary>
    /// Cost and revenue per animal, and per herd (by current location), for a range.
    ///
    /// One response carries both tables and the arithmetic behind them: each figure is
    /// either a record that names the animal or a stated fraction of a pool, the pool
    /// allocations travel with their numerator and denominator, and money that reached no
    /// animal is reported as unallocated rather than spread. See
    /// <see cref="ICostAttributionService"/> for the rules.
    /// </summary>
    [HttpGet("cost-per-animal")]
    public async Task<IActionResult> GetCostPerAnimalReport(
        Guid farmId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _costAttribution.GetCostPerAnimalReportAsync(
            farmId, new CostReportFilter { From = from, To = to });

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
