using FMS.Application.Common;
using FMS.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/inventory")]
[Authorize(Roles = "Employee,FarmManager,SystemOwner")]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _service;
    public SuppliersController(ISupplierService service) => _service = service;

    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers(Guid farmId, [FromQuery] SupplierListFilter filter) => Map(await _service.GetSuppliersAsync(farmId, filter));
    [HttpGet("suppliers/{id:guid}")]
    public async Task<IActionResult> GetSupplier(Guid farmId, Guid id) => Map(await _service.GetSupplierAsync(farmId, id));
    [HttpPost("suppliers")]
    public async Task<IActionResult> CreateSupplier(Guid farmId, CreateSupplierRequest request)
    {
        var result = await _service.CreateSupplierAsync(farmId, request);
        return result.IsSuccess ? CreatedAtAction(nameof(GetSupplier), new { farmId, id = result.Value!.Id }, result.Value) : Map(result);
    }
    [HttpPut("suppliers/{id:guid}")]
    public async Task<IActionResult> UpdateSupplier(Guid farmId, Guid id, UpdateSupplierRequest request) => Map(await _service.UpdateSupplierAsync(farmId, id, request));
    [HttpDelete("suppliers/{id:guid}")]
    public async Task<IActionResult> DeleteSupplier(Guid farmId, Guid id)
    {
        var result = await _service.DeleteSupplierAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }
    [HttpGet("supplier-purchases")]
    public async Task<IActionResult> GetPurchases(Guid farmId, [FromQuery] SupplierPurchaseListFilter filter) => Map(await _service.GetPurchasesAsync(farmId, filter));
    [HttpPost("supplier-purchases")]
    public async Task<IActionResult> CreatePurchase(Guid farmId, CreateSupplierPurchaseRequest request)
    {
        var result = await _service.CreatePurchaseAsync(farmId, request);
        return result.IsSuccess ? Created("", result.Value) : Map(result);
    }

    private IActionResult Map<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : MapError(result.Error!);
    private IActionResult MapError(Error error) => error.Code switch { "NotFound" => NotFound(error.Message), "Validation" => BadRequest(error.Message), "Conflict" => Conflict(error.Message), "Unauthorized" => Unauthorized(error.Message), _ => StatusCode(500, error.Message) };
}
