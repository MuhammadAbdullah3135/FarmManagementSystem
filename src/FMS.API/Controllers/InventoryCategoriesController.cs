using FMS.Application.Common;
using FMS.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

/// <summary>
/// Inventory categories are readable by any member of the farm. Writes use the same
/// roles as inventory-item writes, because they change the vocabulary those items use.
/// </summary>
[ApiController]
[Route("api/farm/{farmId:guid}/inventory/categories")]
[Authorize]
public class InventoryCategoriesController : ControllerBase
{
    private const string InventoryWriteRoles = "Employee,Veterinarian,FarmManager,SystemOwner";

    private readonly IInventoryCategoryService _categoryService;

    public InventoryCategoriesController(IInventoryCategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories(Guid farmId, [FromQuery] bool includeInactive = false)
        => MapResult(await _categoryService.GetCategoriesAsync(farmId, includeInactive));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCategory(Guid farmId, Guid id)
        => MapResult(await _categoryService.GetCategoryByIdAsync(farmId, id));

    [HttpPost]
    [Authorize(Roles = InventoryWriteRoles)]
    public async Task<IActionResult> CreateCategory(Guid farmId, [FromBody] CreateInventoryCategoryRequest request)
    {
        var result = await _categoryService.CreateCategoryAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetCategory), new { farmId, id = result.Value!.Id }, result.Value)
            : MapError(result.Error!);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = InventoryWriteRoles)]
    public async Task<IActionResult> UpdateCategory(
        Guid farmId, Guid id, [FromBody] UpdateInventoryCategoryRequest request)
        => MapResult(await _categoryService.UpdateCategoryAsync(farmId, id, request));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = InventoryWriteRoles)]
    public async Task<IActionResult> DeleteCategory(Guid farmId, Guid id)
    {
        var result = await _categoryService.DeleteCategoryAsync(farmId, id);
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
