using FMS.Application.Common;
using FMS.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// Vets are readable and writable by any member of the farm, following the existing
/// clinical-record access rule. Farm membership and role resolution stay in
/// <c>FarmContextMiddleware</c>; this controller adds no stricter role filter.
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/vets")]
[Authorize]
public class VetsController : ControllerBase
{
    private readonly IVetService _vetService;

    public VetsController(IVetService vetService)
    {
        _vetService = vetService;
    }

    [HttpGet]
    public async Task<IActionResult> GetVets(Guid farmId, [FromQuery] bool includeInactive = false)
        => MapResult(await _vetService.GetVetsAsync(farmId, includeInactive));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetVet(Guid farmId, Guid id)
        => MapResult(await _vetService.GetVetByIdAsync(farmId, id));

    [HttpPost]
    public async Task<IActionResult> CreateVet(Guid farmId, [FromBody] CreateVetRequest request)
    {
        var result = await _vetService.CreateVetAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetVet), new { farmId, id = result.Value!.Id }, result.Value)
            : MapError(result.Error!);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateVet(
        Guid farmId, Guid id, [FromBody] UpdateVetRequest request)
        => MapResult(await _vetService.UpdateVetAsync(farmId, id, request));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteVet(Guid farmId, Guid id)
    {
        var result = await _vetService.DeleteVetAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    private IActionResult MapResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : MapError(result.Error!);

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
