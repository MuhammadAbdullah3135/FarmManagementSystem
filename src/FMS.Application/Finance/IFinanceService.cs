using FMS.Application.Common;

namespace FMS.Application.Finance;

public interface IFinanceService
{
    // Expense categories
    Task<Result<List<ExpenseCategoryDto>>> GetExpenseCategoriesAsync(Guid farmId);
    Task<Result<ExpenseCategoryDto>> CreateExpenseCategoryAsync(Guid farmId, CreateExpenseCategoryRequest request);
    Task<Result<ExpenseCategoryDto>> UpdateExpenseCategoryAsync(Guid farmId, Guid id, UpdateExpenseCategoryRequest request);
    Task<Result> DeleteExpenseCategoryAsync(Guid farmId, Guid id);

    // Payment methods
    Task<Result<List<PaymentMethodDto>>> GetPaymentMethodsAsync(Guid farmId);
    Task<Result<PaymentMethodDto>> CreatePaymentMethodAsync(Guid farmId, CreatePaymentMethodRequest request);
    Task<Result<PaymentMethodDto>> UpdatePaymentMethodAsync(Guid farmId, Guid id, UpdatePaymentMethodRequest request);
    Task<Result> DeletePaymentMethodAsync(Guid farmId, Guid id);

    // Expenses
    Task<Result<ExpenseDto>> GetExpenseByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<ExpenseDto>>> GetExpensesAsync(Guid farmId, ExpenseListFilter filter);
    Task<Result<ExpenseDto>> CreateExpenseAsync(Guid farmId, CreateExpenseRequest request);

    /// <summary>
    /// Validates a batch of expenses without writing it, using the same rules and the
    /// same reference checks as <see cref="CreateExpenseAsync"/>. There is deliberately
    /// no duplicate check: an expense has no identifier and the create path enforces
    /// none, so the bulk path must not be stricter than the endpoint.
    /// </summary>
    Task<Result<BulkCreateResultDto>> ValidateExpensesAsync(Guid farmId, IReadOnlyList<CreateExpenseRequest> requests);

    /// <summary>Creates the whole batch in one SaveChanges, so it is atomic.</summary>
    Task<Result<BulkCreateResultDto>> CreateExpensesAsync(Guid farmId, IReadOnlyList<CreateExpenseRequest> requests);
    Task<Result<ExpenseDto>> UpdateExpenseAsync(Guid farmId, Guid id, UpdateExpenseRequest request);
    Task<Result> DeleteExpenseAsync(Guid farmId, Guid id);

    // Income categories
    Task<Result<List<IncomeCategoryDto>>> GetIncomeCategoriesAsync(Guid farmId);
    Task<Result<IncomeCategoryDto>> CreateIncomeCategoryAsync(Guid farmId, CreateIncomeCategoryRequest request);
    Task<Result<IncomeCategoryDto>> UpdateIncomeCategoryAsync(Guid farmId, Guid id, UpdateIncomeCategoryRequest request);
    Task<Result> DeleteIncomeCategoryAsync(Guid farmId, Guid id);

    // Income records
    Task<Result<IncomeRecordDto>> GetIncomeRecordByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<IncomeRecordDto>>> GetIncomeRecordsAsync(Guid farmId, IncomeRecordListFilter filter);
    Task<Result<IncomeRecordDto>> CreateIncomeRecordAsync(Guid farmId, CreateIncomeRecordRequest request);

    /// <summary>
    /// Validates a batch of income records without writing it, using the same rules and
    /// the same reference checks as <see cref="CreateIncomeRecordAsync"/>. There is
    /// deliberately no duplicate check: an income record has no identifier and the
    /// create path enforces none.
    /// </summary>
    Task<Result<BulkCreateResultDto>> ValidateIncomeRecordsAsync(Guid farmId, IReadOnlyList<CreateIncomeRecordRequest> requests);

    /// <summary>Creates the whole batch in one SaveChanges, so it is atomic.</summary>
    Task<Result<BulkCreateResultDto>> CreateIncomeRecordsAsync(Guid farmId, IReadOnlyList<CreateIncomeRecordRequest> requests);
    Task<Result<IncomeRecordDto>> UpdateIncomeRecordAsync(Guid farmId, Guid id, UpdateIncomeRecordRequest request);
    Task<Result> DeleteIncomeRecordAsync(Guid farmId, Guid id);

    // Reports
    Task<Result<ProfitLossReport>> GetProfitLossReportAsync(Guid farmId, FinanceReportFilter filter);
    Task<Result<CategoryBreakdownReport>> GetExpenseBreakdownAsync(Guid farmId, FinanceReportFilter filter);
    Task<Result<CategoryBreakdownReport>> GetIncomeBreakdownAsync(Guid farmId, FinanceReportFilter filter);
    Task<Result<List<MonthlySummaryItem>>> GetMonthlySummaryAsync(Guid farmId, int year);
}
