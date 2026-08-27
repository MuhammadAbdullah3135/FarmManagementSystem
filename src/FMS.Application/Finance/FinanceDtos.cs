using FMS.Application.Common;

namespace FMS.Application.Finance;

public class ExpenseCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ExpenseCount { get; set; }
}

public class CreateExpenseCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateExpenseCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class PaymentMethodDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ExpenseCount { get; set; }
    public int IncomeRecordCount { get; set; }
}

public class CreatePaymentMethodRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdatePaymentMethodRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ExpenseDto
{
    public Guid Id { get; set; }
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public Guid ExpenseCategoryId { get; set; }
    public string ExpenseCategoryName { get; set; } = string.Empty;
    public Guid PaymentMethodId { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public Guid? AnimalId { get; set; }
    public string? AnimalTagNumber { get; set; }
    public string? AnimalName { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? Description { get; set; }
}

public class CreateExpenseRequest
{
    public DateTime? ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public Guid ExpenseCategoryId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Description { get; set; }
}

public class UpdateExpenseRequest
{
    public DateTime ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public Guid ExpenseCategoryId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Description { get; set; }
}

public class ExpenseListFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? ExpenseCategoryId { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// Income categories

public class IncomeCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int IncomeRecordCount { get; set; }
}

public class CreateIncomeCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateIncomeCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

// Income records

public class IncomeRecordDto
{
    public Guid Id { get; set; }
    public DateTime IncomeDate { get; set; }
    public decimal Amount { get; set; }
    public Guid IncomeCategoryId { get; set; }
    public string IncomeCategoryName { get; set; } = string.Empty;
    public Guid PaymentMethodId { get; set; }
    public string PaymentMethodName { get; set; } = string.Empty;
    public Guid? AnimalId { get; set; }
    public string? AnimalTagNumber { get; set; }
    public string? AnimalName { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public string? Description { get; set; }
}

public class CreateIncomeRecordRequest
{
    public DateTime? IncomeDate { get; set; }
    public decimal Amount { get; set; }
    public Guid IncomeCategoryId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Description { get; set; }
}

public class UpdateIncomeRecordRequest
{
    public DateTime IncomeDate { get; set; }
    public decimal Amount { get; set; }
    public Guid IncomeCategoryId { get; set; }
    public Guid PaymentMethodId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Description { get; set; }
}

public class IncomeRecordListFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? IncomeCategoryId { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// Reports

public class ProfitLossReport
{
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetProfit { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class CategoryBreakdownItem
{
    public Guid CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Percentage { get; set; }
}

public class CategoryBreakdownReport
{
    public List<CategoryBreakdownItem> Items { get; set; } = new();
    public decimal GrandTotal { get; set; }
}

public class MonthlySummaryItem
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
}

public class FinanceReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
