using FMS.Application.Common;
using FMS.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FMS.API.Controllers;

[ApiController]
[Route("api/farm/{farmId:guid}/finance")]
[Authorize]
public class FinanceController : ControllerBase
{
    private readonly IFinanceService _financeService;

    public FinanceController(IFinanceService financeService)
    {
        _financeService = financeService;
    }

    // Expense categories

    [HttpGet("expense-categories")]
    public async Task<IActionResult> GetExpenseCategories(Guid farmId)
    {
        var result = await _financeService.GetExpenseCategoriesAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("expense-categories")]
    public async Task<IActionResult> CreateExpenseCategory(Guid farmId, [FromBody] CreateExpenseCategoryRequest request)
    {
        var result = await _financeService.CreateExpenseCategoryAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetExpenseCategories), new { farmId }, result.Value)
            : MapResult(result);
    }

    [HttpPut("expense-categories/{id:guid}")]
    public async Task<IActionResult> UpdateExpenseCategory(Guid farmId, Guid id, [FromBody] UpdateExpenseCategoryRequest request)
    {
        var result = await _financeService.UpdateExpenseCategoryAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("expense-categories/{id:guid}")]
    public async Task<IActionResult> DeleteExpenseCategory(Guid farmId, Guid id)
    {
        var result = await _financeService.DeleteExpenseCategoryAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // Payment methods

    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods(Guid farmId)
    {
        var result = await _financeService.GetPaymentMethodsAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("payment-methods")]
    public async Task<IActionResult> CreatePaymentMethod(Guid farmId, [FromBody] CreatePaymentMethodRequest request)
    {
        var result = await _financeService.CreatePaymentMethodAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetPaymentMethods), new { farmId }, result.Value)
            : MapResult(result);
    }

    [HttpPut("payment-methods/{id:guid}")]
    public async Task<IActionResult> UpdatePaymentMethod(Guid farmId, Guid id, [FromBody] UpdatePaymentMethodRequest request)
    {
        var result = await _financeService.UpdatePaymentMethodAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("payment-methods/{id:guid}")]
    public async Task<IActionResult> DeletePaymentMethod(Guid farmId, Guid id)
    {
        var result = await _financeService.DeletePaymentMethodAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // Expenses

    [HttpGet("expenses")]
    public async Task<IActionResult> GetExpenses(Guid farmId, [FromQuery] ExpenseListFilter filter)
    {
        var result = await _financeService.GetExpensesAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("expenses/{id:guid}")]
    public async Task<IActionResult> GetExpense(Guid farmId, Guid id)
    {
        var result = await _financeService.GetExpenseByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense(Guid farmId, [FromBody] CreateExpenseRequest request)
    {
        var result = await _financeService.CreateExpenseAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetExpense), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("expenses/{id:guid}")]
    public async Task<IActionResult> UpdateExpense(Guid farmId, Guid id, [FromBody] UpdateExpenseRequest request)
    {
        var result = await _financeService.UpdateExpenseAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("expenses/{id:guid}")]
    public async Task<IActionResult> DeleteExpense(Guid farmId, Guid id)
    {
        var result = await _financeService.DeleteExpenseAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // Income categories

    [HttpGet("income-categories")]
    public async Task<IActionResult> GetIncomeCategories(Guid farmId)
    {
        var result = await _financeService.GetIncomeCategoriesAsync(farmId);
        return MapResult(result);
    }

    [HttpPost("income-categories")]
    public async Task<IActionResult> CreateIncomeCategory(Guid farmId, [FromBody] CreateIncomeCategoryRequest request)
    {
        var result = await _financeService.CreateIncomeCategoryAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetIncomeCategories), new { farmId }, result.Value)
            : MapResult(result);
    }

    [HttpPut("income-categories/{id:guid}")]
    public async Task<IActionResult> UpdateIncomeCategory(Guid farmId, Guid id, [FromBody] UpdateIncomeCategoryRequest request)
    {
        var result = await _financeService.UpdateIncomeCategoryAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("income-categories/{id:guid}")]
    public async Task<IActionResult> DeleteIncomeCategory(Guid farmId, Guid id)
    {
        var result = await _financeService.DeleteIncomeCategoryAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // Income records

    [HttpGet("income-records")]
    public async Task<IActionResult> GetIncomeRecords(Guid farmId, [FromQuery] IncomeRecordListFilter filter)
    {
        var result = await _financeService.GetIncomeRecordsAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("income-records/{id:guid}")]
    public async Task<IActionResult> GetIncomeRecord(Guid farmId, Guid id)
    {
        var result = await _financeService.GetIncomeRecordByIdAsync(farmId, id);
        return MapResult(result);
    }

    [HttpPost("income-records")]
    public async Task<IActionResult> CreateIncomeRecord(Guid farmId, [FromBody] CreateIncomeRecordRequest request)
    {
        var result = await _financeService.CreateIncomeRecordAsync(farmId, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetIncomeRecord), new { farmId, id = result.Value!.Id }, result.Value)
            : MapResult(result);
    }

    [HttpPut("income-records/{id:guid}")]
    public async Task<IActionResult> UpdateIncomeRecord(Guid farmId, Guid id, [FromBody] UpdateIncomeRecordRequest request)
    {
        var result = await _financeService.UpdateIncomeRecordAsync(farmId, id, request);
        return MapResult(result);
    }

    [HttpDelete("income-records/{id:guid}")]
    public async Task<IActionResult> DeleteIncomeRecord(Guid farmId, Guid id)
    {
        var result = await _financeService.DeleteIncomeRecordAsync(farmId, id);
        return result.IsSuccess ? NoContent() : MapError(result.Error!);
    }

    // Reports

    [HttpGet("reports/profit-loss")]
    public async Task<IActionResult> GetProfitLoss(Guid farmId, [FromQuery] FinanceReportFilter filter)
    {
        var result = await _financeService.GetProfitLossReportAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("reports/expense-breakdown")]
    public async Task<IActionResult> GetExpenseBreakdown(Guid farmId, [FromQuery] FinanceReportFilter filter)
    {
        var result = await _financeService.GetExpenseBreakdownAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("reports/income-breakdown")]
    public async Task<IActionResult> GetIncomeBreakdown(Guid farmId, [FromQuery] FinanceReportFilter filter)
    {
        var result = await _financeService.GetIncomeBreakdownAsync(farmId, filter);
        return MapResult(result);
    }

    [HttpGet("reports/monthly-summary")]
    public async Task<IActionResult> GetMonthlySummary(Guid farmId, [FromQuery] int year)
    {
        if (year < 2000 || year > 2100) year = DateTime.UtcNow.Year;
        var result = await _financeService.GetMonthlySummaryAsync(farmId, year);
        return MapResult(result);
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
