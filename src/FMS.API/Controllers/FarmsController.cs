using FMS.Application.Farm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FarmsController : ControllerBase
{
    private readonly IFarmService _farmService;

    public FarmsController(IFarmService farmService)
    {
        _farmService = farmService;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private Guid GetAccountId() => Guid.Parse(User.FindFirst("accountId")!.Value);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFarmRequest request)
    {
        var result = await _farmService.CreateFarmAsync(GetAccountId(), GetUserId(), request);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);

        return result.Error?.Code switch
        {
            "Validation" => BadRequest(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _farmService.GetFarmAsync(id, GetUserId());

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error?.Code switch
        {
            "NotFound" => NotFound(result.Error.Message),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetUserFarms()
    {
        var result = await _farmService.GetUserFarmsAsync(GetAccountId(), GetUserId());

        if (result.IsSuccess)
            return Ok(result.Value);

        return StatusCode(500, result.Error?.Message);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFarmRequest request)
    {
        var result = await _farmService.UpdateFarmAsync(id, GetUserId(), request);

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error?.Code switch
        {
            "NotFound" => NotFound(result.Error.Message),
            "Unauthorized" => Forbid(),
            _ => StatusCode(500, result.Error?.Message)
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _farmService.DeleteFarmAsync(id, GetUserId());

        if (result.IsSuccess)
            return NoContent();

        return result.Error?.Code switch
        {
            "NotFound" => NotFound(result.Error.Message),
            "Unauthorized" => Forbid(),
            _ => StatusCode(500, result.Error?.Message)
        };
    }
}
