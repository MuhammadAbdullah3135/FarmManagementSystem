using FMS.Application.Common;
using FMS.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}")]
[Authorize]
public class VaccinesController : ControllerBase
{
    private readonly IVaccineService _vaccineService;

    public VaccinesController(IVaccineService vaccineService)
    {
        _vaccineService = vaccineService;
    }

    // ── VaccineType CRUD ──────────────────────────────────

    [HttpGet("vaccines")]
    public async Task<IActionResult> GetVaccineTypes(Guid farmId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var result = await _vaccineService.GetVaccineTypesAsync(farmId, page, pageSize, search);
        return MapResult(result);
    }

    [HttpGet("vaccines/{id:guid}")]
    public async Task<IActionResult> GetVaccineType(Guid farmId, Guid id)
    {
        var result = await _vaccineService.GetVaccineTypeByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost("vaccines")]
    public async Task<IActionResult> CreateVaccineType(Guid farmId, [FromBody] CreateVaccineTypeRequest request)
    {
        var result = await _vaccineService.CreateVaccineTypeAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetVaccineType), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("vaccines/{id:guid}")]
    public async Task<IActionResult> UpdateVaccineType(Guid farmId, Guid id, [FromBody] UpdateVaccineTypeRequest request)
    {
        var result = await _vaccineService.UpdateVaccineTypeAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("vaccines/{id:guid}")]
    public async Task<IActionResult> DeleteVaccineType(Guid farmId, Guid id)
    {
        var result = await _vaccineService.DeleteVaccineTypeAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // ── VaccinationRecord CRUD ────────────────────────────

    [HttpGet("vaccinations")]
    public async Task<IActionResult> GetVaccinationRecords(Guid farmId, [FromQuery] VaccinationRecordListFilter filter)
    {
        var result = await _vaccineService.GetVaccinationRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("vaccinations/{id:guid}")]
    public async Task<IActionResult> GetVaccinationRecord(Guid farmId, Guid id)
    {
        var result = await _vaccineService.GetVaccinationRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpGet("vaccinations/animal/{animalId:guid}")]
    public async Task<IActionResult> GetVaccinationRecordsByAnimal(Guid farmId, Guid animalId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _vaccineService.GetVaccinationRecordsByAnimalAsync(farmId, animalId, page, pageSize);
        return MapResult(result);
    }

    [HttpPost("vaccinations")]
    public async Task<IActionResult> CreateVaccinationRecord(Guid farmId, [FromBody] CreateVaccinationRecordRequest request)
    {
        var result = await _vaccineService.CreateVaccinationRecordAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetVaccinationRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("vaccinations/{id:guid}")]
    public async Task<IActionResult> UpdateVaccinationRecord(Guid farmId, Guid id, [FromBody] UpdateVaccinationRecordRequest request)
    {
        var result = await _vaccineService.UpdateVaccinationRecordAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("vaccinations/{id:guid}")]
    public async Task<IActionResult> DeleteVaccinationRecord(Guid farmId, Guid id)
    {
        var result = await _vaccineService.DeleteVaccinationRecordAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // ── VaccinationSchedule CRUD ──────────────────────────

    [HttpGet("vaccinations/schedule")]
    public async Task<IActionResult> GetSchedules(Guid farmId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _vaccineService.GetSchedulesAsync(farmId, page, pageSize);
        return MapResult(result);
    }

    [HttpPost("vaccinations/schedule")]
    public async Task<IActionResult> CreateSchedule(Guid farmId, [FromBody] CreateVaccinationScheduleRequest request)
    {
        var result = await _vaccineService.CreateScheduleAsync(farmId, request);
        return result.IsSuccess
            ? Ok(result.Value)
            : MapResult(result);
    }

    [HttpPut("vaccinations/schedule/{id:guid}")]
    public async Task<IActionResult> UpdateSchedule(Guid farmId, Guid id, [FromBody] UpdateVaccinationScheduleRequest request)
    {
        var result = await _vaccineService.UpdateScheduleAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("vaccinations/schedule/{id:guid}")]
    public async Task<IActionResult> DeleteSchedule(Guid farmId, Guid id)
    {
        var result = await _vaccineService.DeleteScheduleAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // ── Vaccination Status ────────────────────────────────

    [HttpGet("vaccinations/status")]
    public async Task<IActionResult> GetVaccinationStatus(Guid farmId)
    {
        var result = await _vaccineService.GetVaccinationStatusAsync(farmId);
        return MapResult(result);
    }

    [HttpGet("vaccinations/status/overdue")]
    public async Task<IActionResult> GetOverdueVaccinations(Guid farmId)
    {
        var result = await _vaccineService.GetOverdueVaccinationsAsync(farmId);
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
