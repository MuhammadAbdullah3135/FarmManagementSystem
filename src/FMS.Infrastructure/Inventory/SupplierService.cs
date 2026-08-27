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
        var error = ValidateSupplier(request);
        if (error != null) return Result<SupplierDto>.Validation(error);
        var name = request.Name.Trim();
        if (await _context.Suppliers.AnyAsync(s => s.FarmId == farmId && s.Name == name)) return Result<SupplierDto>.Conflict("A supplier with this name already exists");
        var supplier = new Supplier { FarmId = farmId, Name = name, ContactInfo = Clean(request.ContactInfo), ProductsSupplied = Clean(request.ProductsSupplied), CreatedBy = _currentUser.GetUserId() };
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();
        return await GetSupplierAsync(farmId, supplier.Id);
    }

    public async Task<Result<SupplierDto>> UpdateSupplierAsync(Guid farmId, Guid id, UpdateSupplierRequest request)
    {
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Id == id);
        if (supplier == null) return Result<SupplierDto>.NotFound("Supplier not found");
        var error = ValidateSupplier(request);
        if (error != null) return Result<SupplierDto>.Validation(error);
        var name = request.Name.Trim();
        if (await _context.Suppliers.AnyAsync(s => s.FarmId == farmId && s.Id != id && s.Name == name)) return Result<SupplierDto>.Conflict("A supplier with this name already exists");
        supplier.Name = name; supplier.ContactInfo = Clean(request.ContactInfo); supplier.ProductsSupplied = Clean(request.ProductsSupplied); supplier.ModifiedAt = DateTime.UtcNow; supplier.ModifiedBy = _currentUser.GetUserId();
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
        var purchase = new SupplierPurchase { FarmId = farmId, SupplierId = supplier.Id, InventoryItemId = item.Id, Quantity = request.Quantity, TotalCost = request.TotalCost, PurchaseDate = date, StockMovementId = movement.Id, Expense = expense, Notes = Clean(request.Notes), CreatedBy = userId };        purchase.StockMovement = movement;
        _context.SupplierPurchases.Add(purchase);

        await _context.SaveChangesAsync(); await transaction.CommitAsync();
        return Result<SupplierPurchaseDto>.Success(ToDto(purchase, supplier, item, unitCost));
    }

    private static string? ValidateSupplier(CreateSupplierRequest request) { if (string.IsNullOrWhiteSpace(request.Name)) return "Supplier name is required"; if (request.Name.Trim().Length > 200) return "Supplier name cannot exceed 200 characters"; if (request.ContactInfo?.Length > 1000) return "Contact information cannot exceed 1000 characters"; if (request.ProductsSupplied?.Length > 2000) return "Products supplied cannot exceed 2000 characters"; return null; }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static SupplierPurchaseDto ToDto(SupplierPurchase p) => ToDto(p, p.Supplier, p.InventoryItem, p.Quantity == 0 ? 0 : p.TotalCost / p.Quantity);
    private static SupplierPurchaseDto ToDto(SupplierPurchase p, Supplier s, InventoryItem i, decimal unitCost) => new() { Id = p.Id, SupplierId = s.Id, SupplierName = s.Name, InventoryItemId = i.Id, InventoryItemName = i.Name, Unit = i.Unit, Quantity = p.Quantity, TotalCost = p.TotalCost, UnitCost = unitCost, PurchaseDate = p.PurchaseDate, StockMovementId = p.StockMovementId, ExpenseId = p.ExpenseId, Notes = p.Notes };
}
