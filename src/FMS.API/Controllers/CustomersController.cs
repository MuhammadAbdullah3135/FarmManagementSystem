using FMS.Application.Common;
using FMS.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/inventory")]
[Authorize(Roles = "FarmWorker,FarmManager,SystemOwner,Owner")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _service;
    public CustomersController(ICustomerService service) => _service = service;
    [HttpGet("customers")] public async Task<IActionResult> GetCustomers(Guid farmId, [FromQuery] CustomerListFilter filter) => Map(await _service.GetCustomersAsync(farmId, filter));
    [HttpGet("customers/{id:guid}")] public async Task<IActionResult> GetCustomer(Guid farmId, Guid id) => Map(await _service.GetCustomerAsync(farmId, id));
    [HttpPost("customers")] public async Task<IActionResult> CreateCustomer(Guid farmId, CreateCustomerRequest request) { var result = await _service.CreateCustomerAsync(farmId, request); return result.IsSuccess ? CreatedAtAction(nameof(GetCustomer), new { farmId, id = result.Value!.Id }, result.Value) : Map(result); }
    [HttpPut("customers/{id:guid}")] public async Task<IActionResult> UpdateCustomer(Guid farmId, Guid id, UpdateCustomerRequest request) => Map(await _service.UpdateCustomerAsync(farmId, id, request));
    [HttpDelete("customers/{id:guid}")] public async Task<IActionResult> DeleteCustomer(Guid farmId, Guid id) { var result = await _service.DeleteCustomerAsync(farmId, id); return result.IsSuccess ? NoContent() : MapError(result.Error!); }
    [HttpGet("customer-sales")] public async Task<IActionResult> GetSales(Guid farmId, [FromQuery] CustomerSaleListFilter filter) => Map(await _service.GetSalesAsync(farmId, filter));
    [HttpPost("customer-sales")] public async Task<IActionResult> CreateSale(Guid farmId, CreateCustomerSaleRequest request) { var result = await _service.CreateSaleAsync(farmId, request); return result.IsSuccess ? Created("", result.Value) : Map(result); }
    private IActionResult Map<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : MapError(result.Error!);
    private IActionResult MapError(Error error) => error.Code switch { "NotFound" => NotFound(error.Message), "Validation" => BadRequest(error.Message), "Conflict" => Conflict(error.Message), "Unauthorized" => Unauthorized(error.Message), _ => StatusCode(500, error.Message) };
}
