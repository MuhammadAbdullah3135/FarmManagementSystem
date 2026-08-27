using FMS.Application.Common;
using FMS.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/medicines")]
[Authorize]
public class MedicinesController : ControllerBase
{
    private readonly IMedicineService _medicineService;

    public MedicinesController(IMedicineService medicineService)
    {
        _medicineService = medicineService;
    }

    // ── Medicine CRUD ──────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetMedicines(Guid farmId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var result = await _medicineService.GetMedicinesAsync(farmId, page, pageSize, search);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetMedicine(Guid farmId, Guid id)
    {
        var result = await _medicineService.GetMedicineByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateMedicine(Guid farmId, [FromBody] CreateMedicineRequest request)
    {
        var result = await _medicineService.CreateMedicineAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMedicine), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateMedicine(Guid farmId, Guid id, [FromBody] UpdateMedicineRequest request)
    {
        var result = await _medicineService.UpdateMedicineAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteMedicine(Guid farmId, Guid id)
    {
        var result = await _medicineService.DeleteMedicineAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // ── Stock Batches ──────────────────────────────────────

    [HttpGet("{id:guid}/stock")]
    public async Task<IActionResult> GetStockBatches(Guid farmId, Guid id)
    {
        var result = await _medicineService.GetStockBatchesAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost("{id:guid}/stock")]
    public async Task<IActionResult> AddStockBatch(Guid farmId, Guid id, [FromBody] AddStockBatchRequest request)
    {
        var result = await _medicineService.AddStockBatchAsync(farmId, id, request);
        return result.IsSuccess
            ? Ok(result.Value)
            : MapResult(result);
    }

    [HttpDelete("{id:guid}/stock/{stockId:guid}")]
    public async Task<IActionResult> DeleteStockBatch(Guid farmId, Guid id, Guid stockId)
    {
        var result = await _medicineService.DeleteStockBatchAsync(farmId, id, stockId);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // ── Usage ──────────────────────────────────────────────

    [HttpPost("usage")]
    public async Task<IActionResult> RecordUsage(Guid farmId, [FromBody] RecordMedicineUsageRequest request)
    {
        var result = await _medicineService.RecordUsageAsync(farmId, request);
        return result.IsSuccess
            ? Ok(result.Value)
            : MapResult(result);
    }

    // ── Alerts ─────────────────────────────────────────────

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(Guid farmId)
    {
        var result = await _medicineService.GetAlertsAsync(farmId);
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
