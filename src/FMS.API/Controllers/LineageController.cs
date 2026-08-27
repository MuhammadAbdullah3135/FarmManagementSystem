using FMS.Application.Breeding;
using FMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/lineage")]
[Authorize]
public class LineageController : ControllerBase
{
    private readonly IBreedingService _breedingService;

    public LineageController(IBreedingService breedingService)
    {
        _breedingService = breedingService;
    }

    [HttpGet("{animalId:guid}")]
    public async Task<IActionResult> GetLineage(Guid farmId, Guid animalId, [FromQuery] LineageQueryParams query)
    {
        var ancestorDepth = Math.Clamp(query.AncestorDepth, 1, 10);
        var descendantDepth = Math.Clamp(query.DescendantDepth, 1, 10);
        var result = await _breedingService.GetLineageAsync(farmId, animalId, ancestorDepth, descendantDepth);
        return MapResult(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
