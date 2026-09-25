using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Inventory;

public class SupplierService : ISupplierService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SupplierService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<SupplierDto>>> GetSuppliersAsync(Guid farmId, SupplierListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;
        var query = _context.Suppliers.AsNoTracking().Where(s => s.FarmId == farmId);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(s => s.Name.Contains(term) || (s.ProductsSupplied != null && s.ProductsSupplied.Contains(term)));
        }
        var total = await query.CountAsync();
        var suppliers = await query.OrderBy(s => s.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SupplierDto { Id = s.Id, FarmId = s.FarmId, Name = s.Name, ContactInfo = s.ContactInfo, ProductsSupplied = s.ProductsSupplied, PurchaseCount = s.Purchases.Count, TotalPurchased = s.Purchases.Sum(p => (decimal?)p.TotalCost) ?? 0 }).ToListAsync();
        return Result<PagedResult<SupplierDto>>.Success(new PagedResult<SupplierDto> { Items = suppliers, Page = page, PageSize = pageSize, TotalCount = total });
    }

    public async Task<Result<SupplierDto>> GetSupplierAsync(Guid farmId, Guid id)
    {
        var supplier = await _context.Suppliers.AsNoTracking().Where(s => s.FarmId == farmId && s.Id == id)
            .Select(s => new SupplierDto { Id = s.Id, FarmId = s.FarmId, Name = s.Name, ContactInfo = s.ContactInfo, ProductsSupplied = s.ProductsSupplied, PurchaseCount = s.Purchases.Count, TotalPurchased = s.Purchases.Sum(p => (decimal?)p.TotalCost) ?? 0 }).FirstOrDefaultAsync();
        return supplier == null ? Result<SupplierDto>.NotFound("Supplier not found") : Result<SupplierDto>.Success(supplier);
    }

    public async Task<Result<SupplierDto>> CreateSupplierAsync(Guid farmId, CreateSupplierRequest request)
    {
        // One supplier, the endpoint's own path: the rules come from SupplierRules,
        // shared with the bulk import, and the answer is still the first message.
        var errors = SupplierRules.Validate(request.Name, request.ContactInfo, request.ProductsSupplied);
        if (errors.Count > 0) return Result<SupplierDto>.Validation(errors[0].Message, errors[0].MessageKey, errors[0].MessageArgs);
        var name = request.Name.Trim();
        if (await _context.Suppliers.AnyAsync(s => s.FarmId == farmId && s.Name == name)) return Result<SupplierDto>.Conflict(SupplierRules.DuplicateNameMessage);
        var supplier = SupplierRules.Build(farmId, request, _currentUser.GetUserId());
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();
        return await GetSupplierAsync(farmId, supplier.Id);
    }

    public async Task<Result<SupplierDto>> UpdateSupplierAsync(Guid farmId, Guid id, UpdateSupplierRequest request)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Id == id);
        if (supplier == null) return Result<SupplierDto>.NotFound("Supplier not found");
        var errors = SupplierRules.Validate(request.Name, request.ContactInfo, request.ProductsSupplied);
        if (errors.Count > 0) return Result<SupplierDto>.Validation(errors[0].Message, errors[0].MessageKey, errors[0].MessageArgs);
        var name = request.Name.Trim();
        if (await _context.Suppliers.AnyAsync(s => s.FarmId == farmId && s.Id != id && s.Name == name)) return Result<SupplierDto>.Conflict(SupplierRules.DuplicateNameMessage);
        supplier.Name = name; supplier.ContactInfo = SupplierRules.Clean(request.ContactInfo); supplier.ProductsSupplied = SupplierRules.Clean(request.ProductsSupplied); supplier.ModifiedAt = DateTime.UtcNow; supplier.ModifiedBy = _currentUser.GetUserId();
        await _context.SaveChangesAsync();
        return await GetSupplierAsync(farmId, id);
    }

    public async Task<Result> DeleteSupplierAsync(Guid farmId, Guid id)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Id == id);
        if (supplier == null) return Result.NotFound("Supplier not found");
        if (await _context.SupplierPurchases.AnyAsync(p => p.SupplierId == id)) return Result.Conflict("Cannot delete a supplier with purchase history");
        _context.Suppliers.Remove(supplier); await _context.SaveChangesAsync(); return Result.Success();
    }

    /// <summary>
    /// Validates a batch without writing it, using the same rules and the same duplicate
    /// check as <see cref="CreateSupplierAsync"/>, so what this predicts is what
    /// <see cref="CreateSuppliersAsync"/> does.
    /// </summary>
    public Task<Result<BulkCreateResultDto>> ValidateSuppliersAsync(
        Guid farmId, IReadOnlyList<CreateSupplierRequest> requests) =>
        CheckBatchAsync(farmId, requests);

    /// <summary>
    /// Creates every supplier in a single SaveChanges, which EF wraps in one transaction
    /// — so a batch is atomic: either every supplier is written or none is.
    /// </summary>
    public async Task<Result<BulkCreateResultDto>> CreateSuppliersAsync(
        Guid farmId, IReadOnlyList<CreateSupplierRequest> requests)
    {
        var checkedBatch = await CheckBatchAsync(farmId, requests);
        if (!checkedBatch.IsSuccess)
            return checkedBatch;

        if (checkedBatch.Value!.Failures.Count > 0)
            return checkedBatch;

        var userId = _currentUser.GetUserId();

        foreach (var request in requests)
            _context.Suppliers.Add(SupplierRules.Build(farmId, request, userId));

        await _context.SaveChangesAsync();

        return Result<BulkCreateResultDto>.Success(new BulkCreateResultDto
        {
            RequestedCount = requests.Count,
            SuccessCount = requests.Count,
            Failures = new List<BulkCreateFailureDto>()
        });
    }

    /// <summary>
    /// The batch's own checks: every field rule per row, then the name uniqueness the
    /// unique index on (FarmId, Name) would otherwise enforce with an exception.
    ///
    /// Names already in the farm are loaded in one query rather than one per row, and the
    /// batch's own names are compared against each other too — without that second check
    /// a file naming the same supplier twice would reach SaveChanges and fail the whole
    /// batch with a database error instead of a row-level message.
    /// </summary>
    private async Task<Result<BulkCreateResultDto>> CheckBatchAsync(
        Guid farmId, IReadOnlyList<CreateSupplierRequest> requests)
    {
        var failures = new List<BulkCreateFailureDto>();
        var names = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.Name))
            .Select(request => request.Name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Case-sensitive, matching the endpoint's `s.Name == name` and the index itself.
        var existing = names.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _context.Suppliers
                .Where(supplier => supplier.FarmId == farmId && names.Contains(supplier.Name))
                .Select(supplier => supplier.Name)
                .ToListAsync())
                .ToHashSet(StringComparer.Ordinal);

        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var errors = SupplierRules.Validate(request.Name, request.ContactInfo, request.ProductsSupplied);

            if (errors.Count > 0)
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = errors[0].Message });
                continue;
            }

            var name = request.Name.Trim();

            if (existing.Contains(name))
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = SupplierRules.DuplicateNameMessage });
                continue;
            }

            if (!seenInBatch.Add(name))
            {
                failures.Add(new BulkCreateFailureDto
                    { Index = index, Message = $"The batch contains the same supplier name '{name}' twice" });
            }
        }

        return Result<BulkCreateResultDto>.Success(new BulkCreateResultDto
        {
            RequestedCount = requests.Count,
            SuccessCount = requests.Count - failures.Count,
            Failures = failures
        });
    }

    public async Task<Result<PagedResult<SupplierPurchaseDto>>> GetPurchasesAsync(Guid farmId, SupplierPurchaseListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page; var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;
        var query = _context.SupplierPurchases.AsNoTracking().Include(p => p.Supplier).Include(p => p.InventoryItem).Where(p => p.FarmId == farmId);
        if (filter.SupplierId.HasValue) query = query.Where(p => p.SupplierId == filter.SupplierId.Value);
        if (filter.InventoryItemId.HasValue) query = query.Where(p => p.InventoryItemId == filter.InventoryItemId.Value);
        if (filter.From.HasValue) query = query.Where(p => p.PurchaseDate >= filter.From.Value.Date);
        if (filter.To.HasValue) query = query.Where(p => p.PurchaseDate < filter.To.Value.Date.AddDays(1));
        var total = await query.CountAsync();
        var purchases = await query.OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result<PagedResult<SupplierPurchaseDto>>.Success(new PagedResult<SupplierPurchaseDto> { Items = purchases.Select(ToDto).ToList(), Page = page, PageSize = pageSize, TotalCount = total });
    }

    public async Task<Result<SupplierPurchaseDto>> CreatePurchaseAsync(Guid farmId, CreateSupplierPurchaseRequest request)
    {
        if (request.Quantity <= 0) return Result<SupplierPurchaseDto>.Validation("Quantity must be greater than zero");
        if (request.TotalCost < 0) return Result<SupplierPurchaseDto>.Validation("Total cost cannot be negative");
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Id == request.SupplierId);
        if (supplier == null) return Result<SupplierPurchaseDto>.NotFound("Supplier not found");
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.FarmId == farmId && i.Id == request.InventoryItemId);
        if (item == null) return Result<SupplierPurchaseDto>.NotFound("Inventory item not found");
        var date = request.PurchaseDate ?? DateTime.UtcNow;
        if (date > DateTime.UtcNow.AddMinutes(5)) return Result<SupplierPurchaseDto>.Validation("Purchase date cannot be in the future");
        if (request.ExpenseCategoryId.HasValue && !await _context.ExpenseCategories.AnyAsync(c => c.FarmId == farmId && c.Id == request.ExpenseCategoryId.Value)) return Result<SupplierPurchaseDto>.NotFound("Expense category not found");
        if (request.PaymentMethodId.HasValue && !await _context.PaymentMethods.AnyAsync(m => m.FarmId == farmId && m.Id == request.PaymentMethodId.Value)) return Result<SupplierPurchaseDto>.NotFound("Payment method not found");
        if (request.ExpenseCategoryId.HasValue != request.PaymentMethodId.HasValue) return Result<SupplierPurchaseDto>.Validation("Expense category and payment method must be provided together");

        var userId = _currentUser.GetUserId(); var now = DateTime.UtcNow; var unitCost = request.TotalCost / request.Quantity;
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var movement = new StockMovement { FarmId = farmId, InventoryItemId = item.Id, MovementType = InventoryMovementType.Purchase, Quantity = request.Quantity, MovementDate = date, Reason = $"Supplier purchase from {supplier.Name}", PerformedBy = userId, CreatedBy = userId };
        item.Quantity += request.Quantity; _context.StockMovements.Add(movement);
        Expense? expense = null;
        if (request.ExpenseCategoryId.HasValue)
        {
            expense = new Expense { FarmId = farmId, ExpenseDate = date, Amount = Math.Round(request.TotalCost, 2), ExpenseCategoryId = request.ExpenseCategoryId.Value, PaymentMethodId = request.PaymentMethodId!.Value, Description = $"Purchase from {supplier.Name}: {item.Name}", CreatedBy = userId };
            _context.Expenses.Add(expense);
        }
        var purchase = new SupplierPurchase { FarmId = farmId, SupplierId = supplier.Id, InventoryItemId = item.Id, Quantity = request.Quantity, TotalCost = request.TotalCost, PurchaseDate = date, StockMovementId = movement.Id, Expense = expense, Notes = SupplierRules.Clean(request.Notes), CreatedBy = userId };        purchase.StockMovement = movement;
        _context.SupplierPurchases.Add(purchase);

        await _context.SaveChangesAsync(); await transaction.CommitAsync();
        return Result<SupplierPurchaseDto>.Success(ToDto(purchase, supplier, item, unitCost));
    }

    private static SupplierPurchaseDto ToDto(SupplierPurchase p) => ToDto(p, p.Supplier, p.InventoryItem, p.Quantity == 0 ? 0 : p.TotalCost / p.Quantity);
    private static SupplierPurchaseDto ToDto(SupplierPurchase p, Supplier s, InventoryItem i, decimal unitCost) => new() { Id = p.Id, SupplierId = s.Id, SupplierName = s.Name, InventoryItemId = i.Id, InventoryItemName = i.Name, Unit = i.Unit, Quantity = p.Quantity, TotalCost = p.TotalCost, UnitCost = unitCost, PurchaseDate = p.PurchaseDate, StockMovementId = p.StockMovementId, ExpenseId = p.ExpenseId, Notes = p.Notes };
}
