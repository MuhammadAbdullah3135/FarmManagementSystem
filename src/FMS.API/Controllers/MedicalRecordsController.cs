using FMS.Application.Common;
using FMS.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/medical-records")]
[Authorize]
public class MedicalRecordsController : ControllerBase
{
    private readonly IMedicalRecordService _medicalRecordService;

    public MedicalRecordsController(IMedicalRecordService medicalRecordService)
    {
        _medicalRecordService = medicalRecordService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMedicalRecords(Guid farmId, [FromQuery] MedicalRecordListFilter filter)
    {
        var result = await _medicalRecordService.GetMedicalRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetMedicalRecord(Guid farmId, Guid id)
    {
        var result = await _medicalRecordService.GetMedicalRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpGet("animal/{animalId:guid}")]
    public async Task<IActionResult> GetMedicalRecordsByAnimal(Guid farmId, Guid animalId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _medicalRecordService.GetMedicalRecordsByAnimalAsync(farmId, animalId, page, pageSize);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateMedicalRecord(Guid farmId, [FromBody] CreateMedicalRecordRequest request)
    {
        var result = await _medicalRecordService.CreateMedicalRecordAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMedicalRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateMedicalRecord(Guid farmId, Guid id, [FromBody] UpdateMedicalRecordRequest request)
    {
        var result = await _medicalRecordService.UpdateMedicalRecordAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteMedicalRecord(Guid farmId, Guid id)
    {
        var result = await _medicalRecordService.DeleteMedicalRecordAsync(farmId, id);
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
