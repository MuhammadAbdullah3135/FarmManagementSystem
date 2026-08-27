using FMS.Application.Common;
using FMS.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/inventory-items")]
[Authorize(Roles = "FarmWorker,Vet,FarmManager,SystemOwner,Owner")]
public class InventoryItemsController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryItemsController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetItems(Guid farmId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
        => MapResult(await _inventoryService.GetItemsAsync(farmId, page, pageSize, search));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetItem(Guid farmId, Guid id)
        => MapResult(await _inventoryService.GetItemByIdAsync(farmId, id));

    [HttpPost]
    public async Task<IActionResult> CreateItem(Guid farmId, [FromBody] CreateInventoryItemRequest request)
    {
        var result = await _inventoryService.CreateItemAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetItem), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateItem(Guid farmId, Guid id, [FromBody] UpdateInventoryItemRequest request)
        => MapResult(await _inventoryService.UpdateItemAsync(farmId, id, request));

    [HttpPost("movements")]
    public async Task<IActionResult> RecordMovement(Guid farmId, [FromBody] RecordStockMovementRequest request)
    {
        var result = await _inventoryService.RecordMovementAsync(farmId, request);
        return result.IsSuccess ? Created("", result.Value) : MapResult(result);
    }

    [HttpGet("movements")]
    public async Task<IActionResult> GetMovements(Guid farmId, [FromQuery] Guid? inventoryItemId = null, [FromQuery] string? movementType = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => MapResult(await _inventoryService.GetMovementsAsync(farmId, inventoryItemId, movementType, from, to, page, pageSize));

    [HttpGet("reports")]
    public async Task<IActionResult> GetReport(Guid farmId)
        => MapResult(await _inventoryService.GetReportAsync(farmId));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteItem(Guid farmId, Guid id)
    {
        var result = await _inventoryService.DeleteItemAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : MapError(result.Error!);

    private IActionResult MapError(Error error) => error.Code switch
    {
        "NotFound" => NotFound(error.Message),
        "Validation" => BadRequest(error.Message),
        "Conflict" => Conflict(error.Message),
        "Unauthorized" => Unauthorized(error.Message),
        _ => StatusCode(500, error.Message)
    };
}
