using FMS.Application.Common;
using FMS.Application.Employees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployees(Guid farmId, [FromQuery] EmployeeListFilter filter)
    {
        var result = await _employeeService.GetEmployeesAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetEmployee(Guid farmId, Guid id)
    {
        var result = await _employeeService.GetEmployeeByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateEmployee(Guid farmId, [FromBody] CreateEmployeeRequest request)
    {
        var result = await _employeeService.CreateEmployeeAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetEmployee), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateEmployee(Guid farmId, Guid id, [FromBody] UpdateEmployeeRequest request)
    {
        var result = await _employeeService.UpdateEmployeeAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteEmployee(Guid farmId, Guid id)
    {
        var result = await _employeeService.DeleteEmployeeAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // Salary payments & payroll

    [HttpPost("{id:guid}/salary-payments")]
    public async Task<IActionResult> RecordSalaryPayment(Guid farmId, Guid id, [FromBody] RecordSalaryPaymentRequest request)
    {
        var result = await _employeeService.RecordSalaryPaymentAsync(farmId, id, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetSalaryPayments), new { farmId, id }, result.Value)
            : MapResult(result);
    }

    [HttpGet("{id:guid}/salary-payments")]
    public async Task<IActionResult> GetSalaryPayments(Guid farmId, Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _employeeService.GetSalaryPaymentsAsync(farmId, id, page, pageSize);
        return MapResult(result);
    }

    [HttpDelete("{id:guid}/salary-payments/{paymentId:guid}")]
    public async Task<IActionResult> DeleteSalaryPayment(Guid farmId, Guid id, Guid paymentId)
    {
        var result = await _employeeService.DeleteSalaryPaymentAsync(farmId, id, paymentId);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    [HttpGet("payroll-report")]
    public async Task<IActionResult> GetPayrollReport(Guid farmId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _employeeService.GetPayrollReportAsync(farmId, from, to);
        return MapResult(result);
    }

    private IActionResult MapResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : MapError(result.Error!);

    /// <summary>
    /// Maps a domain failure to its response. <see cref="ApiMessageKeys.Attach"/> adds the
    /// failure's i18n key as a header, so the body — the English text every existing caller
    /// and test already asserts on — is untouched while a keyed client can localise it.
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
