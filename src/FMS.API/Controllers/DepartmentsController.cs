using FMS.Application.Common;
using FMS.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly IEmployeeService _employeeService;

    public DepartmentsController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDepartments(Guid farmId)
    {
        var result = await _employeeService.GetDepartmentsAsync(farmId);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateDepartment(Guid farmId, [FromBody] CreateDepartmentRequest request)
    {
        var result = await _employeeService.CreateDepartmentAsync(farmId, request);
        return result.IsSuccess ? Created("", result.Value) : MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDepartment(Guid farmId, Guid id)
    {
        var result = await _employeeService.DeleteDepartmentAsync(farmId, id);
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
