using FMS.Application.Breeding;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/breeding-records")]
[Authorize]
public class BreedingRecordsController : ControllerBase
{
    private readonly IBreedingService _breedingService;

    public BreedingRecordsController(IBreedingService breedingService)
    {
        _breedingService = breedingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBreedingRecords(Guid farmId, [FromQuery] BreedingRecordListFilter filter)
    {
        var result = await _breedingService.GetBreedingRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBreedingRecord(Guid farmId, Guid id)
    {
        var result = await _breedingService.GetBreedingRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    [Authorize(Roles = "Veterinarian,FarmManager,SystemOwner")]
    public async Task<IActionResult> CreateBreedingRecord(Guid farmId, [FromBody] CreateBreedingRecordRequest request)
    {
        var result = await _breedingService.CreateBreedingRecordAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBreedingRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Veterinarian,FarmManager,SystemOwner")]
    public async Task<IActionResult> UpdateBreedingRecord(Guid farmId, Guid id, [FromBody] UpdateBreedingRecordRequest request)
    {
        var result = await _breedingService.UpdateBreedingRecordAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Veterinarian,FarmManager,SystemOwner")]
    public async Task<IActionResult> DeleteBreedingRecord(Guid farmId, Guid id)
    {
        var result = await _breedingService.DeleteBreedingRecordAsync(farmId, id);
        if (result.IsSuccess)
            return NoContent();

        var error = result.Error!;
        ApiMessageKeys.Attach(Response, error);

        // A refusal that names the rows in the way (Error.Blockers) carries them as a problem
        // extension: the sentence stays the body's `detail` for every existing caller, the key
        // stays in the header, and a keyed client can offer the blocking records themselves
        // instead of a count to go and find.
        return error.Blockers is { Count: > 0 } blockers
            ? Conflict(ProblemWithBlockers(error, blockers))
            : MapError(error);
    }

    /// <summary>The 409 problem body with the blocking rows attached as an extension.</summary>
    private ProblemDetails ProblemWithBlockers(Error error, IReadOnlyList<Blocker> blockers) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Conflict",
        Detail = error.Message,
        Instance = HttpContext.Request.Path,
        Extensions =
        {
            ["traceId"] = HttpContext.TraceIdentifier,
            ["blockers"] = blockers
                .Select(b => new { id = b.Id, kind = b.Kind, label = b.Label })
                .ToList(),
        },
    };

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    /// <summary>
    /// Maps a domain failure to its response. <see cref="ApiMessageKeys.Attach"/> adds the
    /// failure's i18n key as a header, so the body — the English text every existing caller
    /// asserts on — is untouched while a keyed client can localise it. The delete guard on a
    /// mating records were made from is the first breeding failure to carry one.
    /// </summary>
    private IActionResult MapError(Error error)
    {
        ApiMessageKeys.Attach(Response, error);
        return error.Code switch
        {
            "NotFound" => NotFound(error.Message),
            "Validation" => BadRequest(error.Message),
            "Conflict" => Conflict(error.Message),
            "Unauthorized" => Unauthorized(error.Message),
            _ => StatusCode(500, error.Message)
        };
    }
}
