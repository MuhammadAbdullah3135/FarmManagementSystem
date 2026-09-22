using FMS.Application.Common;
using FMS.Application.Finance;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Finance;

public class FinanceService : IFinanceService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public FinanceService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // Expense categories

    public async Task<Result<List<ExpenseCategoryDto>>> GetExpenseCategoriesAsync(Guid farmId)
    {
        var categories = await _context.ExpenseCategories
            .Where(c => c.FarmId == farmId)
            .OrderBy(c => c.Name)
            .Select(c => new ExpenseCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                ExpenseCount = _context.Expenses.Count(e => e.ExpenseCategoryId == c.Id && !e.IsDeleted)
            })
            .ToListAsync();

        return Result<List<ExpenseCategoryDto>>.Success(categories);
    }

    public async Task<Result<ExpenseCategoryDto>> CreateExpenseCategoryAsync(Guid farmId, CreateExpenseCategoryRequest request)
    {
        var validationError = ValidateLookup(request.Name, request.Description);
        if (validationError != null)
            return Result<ExpenseCategoryDto>.Validation(validationError);

        var duplicate = await _context.ExpenseCategories
            .AnyAsync(c => c.FarmId == farmId && c.Name.ToLower() == request.Name.Trim().ToLower());
        if (duplicate)
            return Result<ExpenseCategoryDto>.Conflict($"An expense category named '{request.Name}' already exists");

        var now = DateTime.UtcNow;
        var category = new ExpenseCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.ExpenseCategories.Add(category);
        await _context.SaveChangesAsync();

        return Result<ExpenseCategoryDto>.Success(new ExpenseCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description
        });
    }

    public async Task<Result<ExpenseCategoryDto>> UpdateExpenseCategoryAsync(Guid farmId, Guid id, UpdateExpenseCategoryRequest request)
    {
        var category = await _context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.FarmId == farmId);
        if (category == null)
            return Result<ExpenseCategoryDto>.NotFound("Expense category not found");

        var validationError = ValidateLookup(request.Name, request.Description);
        if (validationError != null)
            return Result<ExpenseCategoryDto>.Validation(validationError);

        var duplicate = await _context.ExpenseCategories
            .AnyAsync(c => c.FarmId == farmId && c.Id != id && c.Name.ToLower() == request.Name.Trim().ToLower());
        if (duplicate)
            return Result<ExpenseCategoryDto>.Conflict($"An expense category named '{request.Name}' already exists");

        category.Name = request.Name.Trim();
        category.Description = request.Description?.Trim();
        category.ModifiedAt = DateTime.UtcNow;
        category.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<ExpenseCategoryDto>.Success(new ExpenseCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description
        });
    }

    public async Task<Result> DeleteExpenseCategoryAsync(Guid farmId, Guid id)
    {
        var category = await _context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.FarmId == farmId);
        if (category == null)
            return Result.NotFound("Expense category not found");

        var hasExpenses = await _context.Expenses.AnyAsync(e => e.ExpenseCategoryId == id);
        if (hasExpenses)
            return Result.Conflict("Cannot delete an expense category that has expenses");

        _context.ExpenseCategories.Remove(category);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Payment methods

    public async Task<Result<List<PaymentMethodDto>>> GetPaymentMethodsAsync(Guid farmId)
    {
        var methods = await _context.PaymentMethods
            .Where(m => m.FarmId == farmId)
            .OrderBy(m => m.Name)
            .Select(m => new PaymentMethodDto
            {
                Id = m.Id,
                Name = m.Name,
                Description = m.Description,
                ExpenseCount = _context.Expenses.Count(e => e.PaymentMethodId == m.Id && !e.IsDeleted),
                IncomeRecordCount = _context.IncomeRecords.Count(r => r.PaymentMethodId == m.Id)
            })
            .ToListAsync();

        return Result<List<PaymentMethodDto>>.Success(methods);
    }

    public async Task<Result<PaymentMethodDto>> CreatePaymentMethodAsync(Guid farmId, CreatePaymentMethodRequest request)
    {
        var validationError = ValidateLookup(request.Name, request.Description);
        if (validationError != null)
            return Result<PaymentMethodDto>.Validation(validationError);

        var duplicate = await _context.PaymentMethods
            .AnyAsync(m => m.FarmId == farmId && m.Name.ToLower() == request.Name.Trim().ToLower());
        if (duplicate)
            return Result<PaymentMethodDto>.Conflict($"A payment method named '{request.Name}' already exists");

        var now = DateTime.UtcNow;
        var method = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.PaymentMethods.Add(method);
        await _context.SaveChangesAsync();

        return Result<PaymentMethodDto>.Success(new PaymentMethodDto
        {
            Id = method.Id,
            Name = method.Name,
            Description = method.Description
        });
    }

    public async Task<Result<PaymentMethodDto>> UpdatePaymentMethodAsync(Guid farmId, Guid id, UpdatePaymentMethodRequest request)
    {
        var method = await _context.PaymentMethods
            .FirstOrDefaultAsync(m => m.Id == id && m.FarmId == farmId);
        if (method == null)
            return Result<PaymentMethodDto>.NotFound("Payment method not found");

        var validationError = ValidateLookup(request.Name, request.Description);
        if (validationError != null)
            return Result<PaymentMethodDto>.Validation(validationError);

        var duplicate = await _context.PaymentMethods
            .AnyAsync(m => m.FarmId == farmId && m.Id != id && m.Name.ToLower() == request.Name.Trim().ToLower());
        if (duplicate)
            return Result<PaymentMethodDto>.Conflict($"A payment method named '{request.Name}' already exists");

        method.Name = request.Name.Trim();
        method.Description = request.Description?.Trim();
        method.ModifiedAt = DateTime.UtcNow;
        method.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<PaymentMethodDto>.Success(new PaymentMethodDto
        {
            Id = method.Id,
            Name = method.Name,
            Description = method.Description
        });
    }

    public async Task<Result> DeletePaymentMethodAsync(Guid farmId, Guid id)
    {
        var method = await _context.PaymentMethods
            .FirstOrDefaultAsync(m => m.Id == id && m.FarmId == farmId);
        if (method == null)
            return Result.NotFound("Payment method not found");

        var hasExpenses = await _context.Expenses.AnyAsync(e => e.PaymentMethodId == id);
        if (hasExpenses)
            return Result.Conflict("Cannot delete a payment method that has expenses");

        _context.PaymentMethods.Remove(method);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Expenses

    public async Task<Result<ExpenseDto>> GetExpenseByIdAsync(Guid farmId, Guid id)
    {
        var expense = await _context.Expenses
            .Include(e => e.Category)
            .Include(e => e.PaymentMethod)
            .Include(e => e.Animal)
            .Include(e => e.Location)
            .FirstOrDefaultAsync(e => e.Id == id && e.FarmId == farmId && !e.IsDeleted);
        if (expense == null)
            return Result<ExpenseDto>.NotFound("Expense not found");

        return Result<ExpenseDto>.Success(MapExpense(expense));
    }

    public async Task<Result<PagedResult<ExpenseDto>>> GetExpensesAsync(Guid farmId, ExpenseListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = _context.Expenses
            .Include(e => e.Category)
            .Include(e => e.PaymentMethod)
            .Include(e => e.Animal)
            .Include(e => e.Location)
            .Where(e => e.FarmId == farmId && !e.IsDeleted);

        if (filter.From.HasValue)
            query = query.Where(e => e.ExpenseDate >= filter.From.Value.Date);
        if (filter.To.HasValue)
            query = query.Where(e => e.ExpenseDate < filter.To.Value.Date.AddDays(1));
        if (filter.ExpenseCategoryId.HasValue)
            query = query.Where(e => e.ExpenseCategoryId == filter.ExpenseCategoryId.Value);
        if (filter.PaymentMethodId.HasValue)
            query = query.Where(e => e.PaymentMethodId == filter.PaymentMethodId.Value);
        if (filter.AnimalId.HasValue)
            query = query.Where(e => e.AnimalId == filter.AnimalId.Value);
        if (filter.LocationId.HasValue)
            query = query.Where(e => e.LocationId == filter.LocationId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(e => e.Description != null && e.Description.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync();

        var expenses = await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<ExpenseDto>>.Success(new PagedResult<ExpenseDto>
        {
            Items = expenses.Select(MapExpense).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<ExpenseDto>> CreateExpenseAsync(Guid farmId, CreateExpenseRequest request)
    {
        var amountError = ValidateAmount(request.Amount);
        if (amountError != null)
            return Result<ExpenseDto>.Validation(amountError);

        var expenseDate = request.ExpenseDate ?? DateTime.UtcNow;
        if (expenseDate > DateTime.UtcNow.AddMinutes(5))
            return Result<ExpenseDto>.Validation("Expense date cannot be in the future");
        if (request.Description?.Length > 1000)
            return Result<ExpenseDto>.Validation("Description cannot exceed 1000 characters");

        var referenceError = await ValidateReferencesAsync(farmId, request.ExpenseCategoryId, request.PaymentMethodId, request.AnimalId, request.LocationId);
        if (referenceError != null)
            return Result<ExpenseDto>.Failure(referenceError);

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            ExpenseDate = expenseDate,
            Amount = Math.Round(request.Amount, 2),
            ExpenseCategoryId = request.ExpenseCategoryId,
            PaymentMethodId = request.PaymentMethodId,
            AnimalId = request.AnimalId,
            LocationId = request.LocationId,
            Description = request.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();

        return await GetExpenseByIdAsync(farmId, expense.Id);
    }

    public async Task<Result<ExpenseDto>> UpdateExpenseAsync(Guid farmId, Guid id, UpdateExpenseRequest request)
    {
        var expense = await _context.Expenses
            .FirstOrDefaultAsync(e => e.Id == id && e.FarmId == farmId && !e.IsDeleted);
        if (expense == null)
            return Result<ExpenseDto>.NotFound("Expense not found");

        var amountError = ValidateAmount(request.Amount);
        if (amountError != null)
            return Result<ExpenseDto>.Validation(amountError);

        if (request.ExpenseDate.HasValue && request.ExpenseDate > DateTime.UtcNow.AddMinutes(5))
            return Result<ExpenseDto>.Validation("Expense date cannot be in the future");
        if (request.Description?.Length > 1000)
            return Result<ExpenseDto>.Validation("Description cannot exceed 1000 characters");

        var referenceError = await ValidateReferencesAsync(farmId, request.ExpenseCategoryId, request.PaymentMethodId, request.AnimalId, request.LocationId);
        if (referenceError != null)
            return Result<ExpenseDto>.Failure(referenceError);

        expense.ExpenseDate = request.ExpenseDate ?? expense.ExpenseDate;
        expense.Amount = Math.Round(request.Amount, 2);
        expense.ExpenseCategoryId = request.ExpenseCategoryId;
        expense.PaymentMethodId = request.PaymentMethodId;
        expense.AnimalId = request.AnimalId;
        expense.LocationId = request.LocationId;
        expense.Description = request.Description?.Trim();
        expense.ModifiedAt = DateTime.UtcNow;
        expense.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await GetExpenseByIdAsync(farmId, id);
    }

    public async Task<Result> DeleteExpenseAsync(Guid farmId, Guid id)
    {
        var expense = await _context.Expenses
            .FirstOrDefaultAsync(e => e.Id == id && e.FarmId == farmId && !e.IsDeleted);
        if (expense == null)
            return Result.NotFound("Expense not found");

        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Income categories

    public async Task<Result<List<IncomeCategoryDto>>> GetIncomeCategoriesAsync(Guid farmId)
    {
        var categories = await _context.IncomeCategories
            .Where(c => c.FarmId == farmId)
            .OrderBy(c => c.Name)
            .Select(c => new IncomeCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                IncomeRecordCount = _context.IncomeRecords.Count(r => r.IncomeCategoryId == c.Id)
            })
            .ToListAsync();

        return Result<List<IncomeCategoryDto>>.Success(categories);
    }

    public async Task<Result<IncomeCategoryDto>> CreateIncomeCategoryAsync(Guid farmId, CreateIncomeCategoryRequest request)
    {
        var validationError = ValidateLookup(request.Name, request.Description);
        if (validationError != null)
            return Result<IncomeCategoryDto>.Validation(validationError);

        var duplicate = await _context.IncomeCategories
            .AnyAsync(c => c.FarmId == farmId && c.Name.ToLower() == request.Name.Trim().ToLower());
        if (duplicate)
            return Result<IncomeCategoryDto>.Conflict($"An income category named '{request.Name}' already exists");

        var now = DateTime.UtcNow;
        var category = new IncomeCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.IncomeCategories.Add(category);
        await _context.SaveChangesAsync();

        return Result<IncomeCategoryDto>.Success(new IncomeCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description
        });
    }

    public async Task<Result<IncomeCategoryDto>> UpdateIncomeCategoryAsync(Guid farmId, Guid id, UpdateIncomeCategoryRequest request)
    {
        var category = await _context.IncomeCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.FarmId == farmId);
        if (category == null)
            return Result<IncomeCategoryDto>.NotFound("Income category not found");

        var validationError = ValidateLookup(request.Name, request.Description);
        if (validationError != null)
            return Result<IncomeCategoryDto>.Validation(validationError);

        var duplicate = await _context.IncomeCategories
            .AnyAsync(c => c.FarmId == farmId && c.Id != id && c.Name.ToLower() == request.Name.Trim().ToLower());
        if (duplicate)
            return Result<IncomeCategoryDto>.Conflict($"An income category named '{request.Name}' already exists");

        category.Name = request.Name.Trim();
        category.Description = request.Description?.Trim();
        category.ModifiedAt = DateTime.UtcNow;
        category.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<IncomeCategoryDto>.Success(new IncomeCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description
        });
    }

    public async Task<Result> DeleteIncomeCategoryAsync(Guid farmId, Guid id)
    {
        var category = await _context.IncomeCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.FarmId == farmId);
        if (category == null)
            return Result.NotFound("Income category not found");

        var hasRecords = await _context.IncomeRecords.AnyAsync(r => r.IncomeCategoryId == id);
        if (hasRecords)
            return Result.Conflict("Cannot delete an income category that has income records");

        _context.IncomeCategories.Remove(category);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Income records

    public async Task<Result<IncomeRecordDto>> GetIncomeRecordByIdAsync(Guid farmId, Guid id)
    {
        var record = await _context.IncomeRecords
            .Include(r => r.Category)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Animal)
            .Include(r => r.Location)
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (record == null)
            return Result<IncomeRecordDto>.NotFound("Income record not found");

        return Result<IncomeRecordDto>.Success(MapIncome(record));
    }

    public async Task<Result<PagedResult<IncomeRecordDto>>> GetIncomeRecordsAsync(Guid farmId, IncomeRecordListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = _context.IncomeRecords
            .Include(r => r.Category)
            .Include(r => r.PaymentMethod)
            .Include(r => r.Animal)
            .Include(r => r.Location)
            .Where(r => r.FarmId == farmId);

        if (filter.From.HasValue)
            query = query.Where(r => r.IncomeDate >= filter.From.Value.Date);
        if (filter.To.HasValue)
            query = query.Where(r => r.IncomeDate < filter.To.Value.Date.AddDays(1));
        if (filter.IncomeCategoryId.HasValue)
            query = query.Where(r => r.IncomeCategoryId == filter.IncomeCategoryId.Value);
        if (filter.PaymentMethodId.HasValue)
            query = query.Where(r => r.PaymentMethodId == filter.PaymentMethodId.Value);
        if (filter.AnimalId.HasValue)
            query = query.Where(r => r.AnimalId == filter.AnimalId.Value);
        if (filter.LocationId.HasValue)
            query = query.Where(r => r.LocationId == filter.LocationId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(r => r.Description != null && r.Description.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderByDescending(r => r.IncomeDate)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<IncomeRecordDto>>.Success(new PagedResult<IncomeRecordDto>
        {
            Items = records.Select(MapIncome).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<IncomeRecordDto>> CreateIncomeRecordAsync(Guid farmId, CreateIncomeRecordRequest request)
    {
        var amountError = ValidateAmount(request.Amount);
        if (amountError != null)
            return Result<IncomeRecordDto>.Validation(amountError);

        var incomeDate = request.IncomeDate ?? DateTime.UtcNow;
        if (incomeDate > DateTime.UtcNow.AddMinutes(5))
            return Result<IncomeRecordDto>.Validation("Income date cannot be in the future");
        if (request.Description?.Length > 1000)
            return Result<IncomeRecordDto>.Validation("Description cannot exceed 1000 characters");

        var referenceError = await ValidateIncomeReferencesAsync(farmId, request.IncomeCategoryId, request.PaymentMethodId, request.AnimalId, request.LocationId);
        if (referenceError != null)
            return Result<IncomeRecordDto>.Failure(referenceError);

        var record = new IncomeRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            IncomeDate = incomeDate,
            Amount = Math.Round(request.Amount, 2),
            IncomeCategoryId = request.IncomeCategoryId,
            PaymentMethodId = request.PaymentMethodId,
            AnimalId = request.AnimalId,
            LocationId = request.LocationId,
            Description = request.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.IncomeRecords.Add(record);
        await _context.SaveChangesAsync();

        return await GetIncomeRecordByIdAsync(farmId, record.Id);
    }

    public async Task<Result<IncomeRecordDto>> UpdateIncomeRecordAsync(Guid farmId, Guid id, UpdateIncomeRecordRequest request)
    {
        var record = await _context.IncomeRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (record == null)
            return Result<IncomeRecordDto>.NotFound("Income record not found");

        var amountError = ValidateAmount(request.Amount);
        if (amountError != null)
            return Result<IncomeRecordDto>.Validation(amountError);

        if (request.IncomeDate.HasValue && request.IncomeDate > DateTime.UtcNow.AddMinutes(5))
            return Result<IncomeRecordDto>.Validation("Income date cannot be in the future");
        if (request.Description?.Length > 1000)
            return Result<IncomeRecordDto>.Validation("Description cannot exceed 1000 characters");

        var referenceError = await ValidateIncomeReferencesAsync(farmId, request.IncomeCategoryId, request.PaymentMethodId, request.AnimalId, request.LocationId);
        if (referenceError != null)
            return Result<IncomeRecordDto>.Failure(referenceError);

        record.IncomeDate = request.IncomeDate ?? record.IncomeDate;
        record.Amount = Math.Round(request.Amount, 2);
        record.IncomeCategoryId = request.IncomeCategoryId;
        record.PaymentMethodId = request.PaymentMethodId;
        record.AnimalId = request.AnimalId;
        record.LocationId = request.LocationId;
        record.Description = request.Description?.Trim();
        record.ModifiedAt = DateTime.UtcNow;
        record.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await GetIncomeRecordByIdAsync(farmId, id);
    }

    public async Task<Result> DeleteIncomeRecordAsync(Guid farmId, Guid id)
    {
        var record = await _context.IncomeRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (record == null)
            return Result.NotFound("Income record not found");

        _context.IncomeRecords.Remove(record);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Reports

    public async Task<Result<ProfitLossReport>> GetProfitLossReportAsync(Guid farmId, FinanceReportFilter filter)
    {
        var from = filter.From?.Date;
        var to = filter.To?.Date.AddDays(1);

        var totalIncome = await _context.IncomeRecords
            .Where(r => r.FarmId == farmId
                && (!from.HasValue || r.IncomeDate >= from.Value)
                && (!to.HasValue || r.IncomeDate < to.Value))
            .SumAsync(r => r.Amount);

        var totalExpenses = await _context.Expenses
            .Where(e => e.FarmId == farmId && !e.IsDeleted
                && (!from.HasValue || e.ExpenseDate >= from.Value)
                && (!to.HasValue || e.ExpenseDate < to.Value))
            .SumAsync(e => e.Amount);

        return Result<ProfitLossReport>.Success(new ProfitLossReport
        {
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            NetProfit = totalIncome - totalExpenses,
            From = filter.From,
            To = filter.To
        });
    }

    public async Task<Result<CategoryBreakdownReport>> GetExpenseBreakdownAsync(Guid farmId, FinanceReportFilter filter)
    {
        var from = filter.From?.Date;
        var to = filter.To?.Date.AddDays(1);

        var items = await _context.Expenses
            .Where(e => e.FarmId == farmId && !e.IsDeleted
                && (!from.HasValue || e.ExpenseDate >= from.Value)
                && (!to.HasValue || e.ExpenseDate < to.Value))
            .GroupBy(e => new { e.ExpenseCategoryId, e.Category.Name })
            .Select(g => new CategoryBreakdownItem
            {
                CategoryId = g.Key.ExpenseCategoryId,
                CategoryName = g.Key.Name,
                Total = g.Sum(e => e.Amount)
            })
            .OrderByDescending(i => i.Total)
            .ToListAsync();

        var grandTotal = items.Sum(i => i.Total);
        foreach (var item in items)
            item.Percentage = grandTotal > 0 ? Math.Round(item.Total / grandTotal * 100, 1) : 0;

        return Result<CategoryBreakdownReport>.Success(new CategoryBreakdownReport
        {
            Items = items,
            GrandTotal = grandTotal
        });
    }

    public async Task<Result<CategoryBreakdownReport>> GetIncomeBreakdownAsync(Guid farmId, FinanceReportFilter filter)
    {
        var from = filter.From?.Date;
        var to = filter.To?.Date.AddDays(1);

        var items = await _context.IncomeRecords
            .Where(r => r.FarmId == farmId
                && (!from.HasValue || r.IncomeDate >= from.Value)
                && (!to.HasValue || r.IncomeDate < to.Value))
            .GroupBy(r => new { r.IncomeCategoryId, r.Category.Name })
            .Select(g => new CategoryBreakdownItem
            {
                CategoryId = g.Key.IncomeCategoryId,
                CategoryName = g.Key.Name,
                Total = g.Sum(r => r.Amount)
            })
            .OrderByDescending(i => i.Total)
            .ToListAsync();

        var grandTotal = items.Sum(i => i.Total);
        foreach (var item in items)
            item.Percentage = grandTotal > 0 ? Math.Round(item.Total / grandTotal * 100, 1) : 0;

        return Result<CategoryBreakdownReport>.Success(new CategoryBreakdownReport
        {
            Items = items,
            GrandTotal = grandTotal
        });
    }

    public async Task<Result<List<MonthlySummaryItem>>> GetMonthlySummaryAsync(Guid farmId, int year)
    {
        // Explicitly UTC: the year boundaries are compared against timestamp-with-time-zone
        // columns, and an Unspecified kind is rejected by PostgreSQL (this used to be a 400
        // on every request for a farm's monthly summary).
        var startDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var incomeByMonth = await _context.IncomeRecords
            .Where(r => r.FarmId == farmId && r.IncomeDate >= startDate && r.IncomeDate < endDate)
            .GroupBy(r => r.IncomeDate.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(r => r.Amount) })
            .ToDictionaryAsync(g => g.Month, g => g.Total);

        var expenseByMonth = await _context.Expenses
            .Where(e => e.FarmId == farmId && !e.IsDeleted && e.ExpenseDate >= startDate && e.ExpenseDate < endDate)
            .GroupBy(e => e.ExpenseDate.Month)
            .Select(g => new { Month = g.Key, Total = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(g => g.Month, g => g.Total);

        var months = new[] { "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December" };

        var items = Enumerable.Range(1, 12).Select(m => new MonthlySummaryItem
        {
            Year = year,
            Month = m,
            MonthName = months[m - 1],
            Income = incomeByMonth.GetValueOrDefault(m),
            Expenses = expenseByMonth.GetValueOrDefault(m),
            Net = incomeByMonth.GetValueOrDefault(m) - expenseByMonth.GetValueOrDefault(m)
        }).ToList();

        return Result<List<MonthlySummaryItem>>.Success(items);
    }

    // Helpers

    private static string? ValidateLookup(string? name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required";
        if (name.Length > 100)
            return "Name cannot exceed 100 characters";
        if (description?.Length > 500)
            return "Description cannot exceed 500 characters";
        return null;
    }

    private static string? ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            return "Amount must be greater than zero";
        return null;
    }

    private async Task<Error?> ValidateReferencesAsync(Guid farmId, Guid expenseCategoryId, Guid paymentMethodId, Guid? animalId, Guid? locationId)
    {
        if (!await _context.ExpenseCategories.AnyAsync(c => c.Id == expenseCategoryId && c.FarmId == farmId))
            return Error.NotFound("Expense category not found");

        if (!await _context.PaymentMethods.AnyAsync(m => m.Id == paymentMethodId && m.FarmId == farmId))
            return Error.NotFound("Payment method not found");

        if (animalId.HasValue &&
            !await _context.Animals.AnyAsync(a => a.Id == animalId.Value && a.FarmId == farmId && !a.IsDeleted))
            return Error.NotFound("Animal not found");

        if (locationId.HasValue &&
            !await _context.Locations.AnyAsync(l => l.Id == locationId.Value && l.FarmId == farmId))
            return Error.NotFound("Location not found");

        return null;
    }

    private async Task<Error?> ValidateIncomeReferencesAsync(Guid farmId, Guid incomeCategoryId, Guid paymentMethodId, Guid? animalId, Guid? locationId)
    {
        if (!await _context.IncomeCategories.AnyAsync(c => c.Id == incomeCategoryId && c.FarmId == farmId))
            return Error.NotFound("Income category not found");

        if (!await _context.PaymentMethods.AnyAsync(m => m.Id == paymentMethodId && m.FarmId == farmId))
            return Error.NotFound("Payment method not found");

        if (animalId.HasValue &&
            !await _context.Animals.AnyAsync(a => a.Id == animalId.Value && a.FarmId == farmId && !a.IsDeleted))
            return Error.NotFound("Animal not found");

        if (locationId.HasValue &&
            !await _context.Locations.AnyAsync(l => l.Id == locationId.Value && l.FarmId == farmId))
            return Error.NotFound("Location not found");

        return null;
    }

    private static ExpenseDto MapExpense(Expense expense) => new()
    {
        Id = expense.Id,
        ExpenseDate = expense.ExpenseDate,
        Amount = expense.Amount,
        ExpenseCategoryId = expense.ExpenseCategoryId,
        ExpenseCategoryName = expense.Category.Name,
        PaymentMethodId = expense.PaymentMethodId,
        PaymentMethodName = expense.PaymentMethod.Name,
        AnimalId = expense.AnimalId,
        AnimalTagNumber = expense.Animal?.TagNumber,
        AnimalName = expense.Animal?.Name,
        LocationId = expense.LocationId,
        LocationName = expense.Location?.Name,
        Description = expense.Description
    };

    private static IncomeRecordDto MapIncome(IncomeRecord record) => new()
    {
        Id = record.Id,
        IncomeDate = record.IncomeDate,
        Amount = record.Amount,
        IncomeCategoryId = record.IncomeCategoryId,
        IncomeCategoryName = record.Category.Name,
        PaymentMethodId = record.PaymentMethodId,
        PaymentMethodName = record.PaymentMethod.Name,
        AnimalId = record.AnimalId,
        AnimalTagNumber = record.Animal?.TagNumber,
        AnimalName = record.Animal?.Name,
        LocationId = record.LocationId,
        LocationName = record.Location?.Name,
        Description = record.Description
    };
}
