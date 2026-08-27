using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Inventory;

public class CustomerService : ICustomerService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;
    public CustomerService(FmsDbContext context, ICurrentUserService currentUser) { _context = context; _currentUser = currentUser; }

    public async Task<Result<PagedResult<CustomerDto>>> GetCustomersAsync(Guid farmId, CustomerListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page; var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;
        var query = _context.Customers.AsNoTracking().Where(c => c.FarmId == farmId);
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var term = filter.Search.Trim(); query = query.Where(c => c.Name.Contains(term) || (c.ContactInfo != null && c.ContactInfo.Contains(term))); }
        var total = await query.CountAsync();
        var customers = await query.OrderBy(c => c.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new CustomerDto { Id = c.Id, FarmId = c.FarmId, Name = c.Name, ContactInfo = c.ContactInfo, SaleCount = c.Sales.Count, TotalSales = c.Sales.Sum(s => (decimal?)s.TotalAmount) ?? 0 }).ToListAsync();
        return Result<PagedResult<CustomerDto>>.Success(new PagedResult<CustomerDto> { Items = customers, Page = page, PageSize = pageSize, TotalCount = total });
    }

    public async Task<Result<CustomerDto>> GetCustomerAsync(Guid farmId, Guid id)
    {
        var customer = await _context.Customers.AsNoTracking().Where(c => c.FarmId == farmId && c.Id == id)
            .Select(c => new CustomerDto { Id = c.Id, FarmId = c.FarmId, Name = c.Name, ContactInfo = c.ContactInfo, SaleCount = c.Sales.Count, TotalSales = c.Sales.Sum(s => (decimal?)s.TotalAmount) ?? 0 }).FirstOrDefaultAsync();
        return customer == null ? Result<CustomerDto>.NotFound("Customer not found") : Result<CustomerDto>.Success(customer);
    }

    public async Task<Result<CustomerDto>> CreateCustomerAsync(Guid farmId, CreateCustomerRequest request)
    {
        var error = ValidatePerson(request.Name, request.ContactInfo, "Customer"); if (error != null) return Result<CustomerDto>.Validation(error);
        var name = request.Name.Trim(); if (await _context.Customers.AnyAsync(c => c.FarmId == farmId && c.Name == name)) return Result<CustomerDto>.Conflict("A customer with this name already exists");
        var customer = new Customer { FarmId = farmId, Name = name, ContactInfo = Clean(request.ContactInfo), CreatedBy = _currentUser.GetUserId() };
        _context.Customers.Add(customer); await _context.SaveChangesAsync(); return await GetCustomerAsync(farmId, customer.Id);
    }

    public async Task<Result<CustomerDto>> UpdateCustomerAsync(Guid farmId, Guid id, UpdateCustomerRequest request)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.FarmId == farmId && c.Id == id); if (customer == null) return Result<CustomerDto>.NotFound("Customer not found");
        var error = ValidatePerson(request.Name, request.ContactInfo, "Customer"); if (error != null) return Result<CustomerDto>.Validation(error);
        var name = request.Name.Trim(); if (await _context.Customers.AnyAsync(c => c.FarmId == farmId && c.Id != id && c.Name == name)) return Result<CustomerDto>.Conflict("A customer with this name already exists");
        customer.Name = name; customer.ContactInfo = Clean(request.ContactInfo); customer.ModifiedAt = DateTime.UtcNow; customer.ModifiedBy = _currentUser.GetUserId(); await _context.SaveChangesAsync(); return await GetCustomerAsync(farmId, id);
    }

    public async Task<Result> DeleteCustomerAsync(Guid farmId, Guid id)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.FarmId == farmId && c.Id == id); if (customer == null) return Result.NotFound("Customer not found");
        if (await _context.CustomerSales.AnyAsync(s => s.CustomerId == id)) return Result.Conflict("Cannot delete a customer with sales history");
        _context.Customers.Remove(customer); await _context.SaveChangesAsync(); return Result.Success();
    }

    public async Task<Result<PagedResult<CustomerSaleDto>>> GetSalesAsync(Guid farmId, CustomerSaleListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page; var pageSize = filter.PageSize is < 1 or > 100 ? 20 : filter.PageSize;
        var query = _context.CustomerSales.AsNoTracking().Include(s => s.Customer).Include(s => s.InventoryItem).Where(s => s.FarmId == farmId);
        if (filter.CustomerId.HasValue) query = query.Where(s => s.CustomerId == filter.CustomerId.Value);
        if (filter.InventoryItemId.HasValue) query = query.Where(s => s.InventoryItemId == filter.InventoryItemId.Value);
        if (filter.From.HasValue) query = query.Where(s => s.SaleDate >= filter.From.Value.Date);
        if (filter.To.HasValue) query = query.Where(s => s.SaleDate < filter.To.Value.Date.AddDays(1));
        var total = await query.CountAsync(); var sales = await query.OrderByDescending(s => s.SaleDate).ThenByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result<PagedResult<CustomerSaleDto>>.Success(new PagedResult<CustomerSaleDto> { Items = sales.Select(ToDto).ToList(), Page = page, PageSize = pageSize, TotalCount = total });
    }

    public async Task<Result<CustomerSaleDto>> CreateSaleAsync(Guid farmId, CreateCustomerSaleRequest request)
    {
        if (request.Quantity <= 0) return Result<CustomerSaleDto>.Validation("Quantity must be greater than zero");
        if (request.TotalAmount < 0) return Result<CustomerSaleDto>.Validation("Total amount cannot be negative");
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.FarmId == farmId && c.Id == request.CustomerId); if (customer == null) return Result<CustomerSaleDto>.NotFound("Customer not found");
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.FarmId == farmId && i.Id == request.InventoryItemId); if (item == null) return Result<CustomerSaleDto>.NotFound("Inventory item not found");
        if (item.Quantity < request.Quantity) return Result<CustomerSaleDto>.Conflict($"Insufficient stock: available quantity is {item.Quantity} {item.Unit}");
        var date = request.SaleDate ?? DateTime.UtcNow; if (date > DateTime.UtcNow.AddMinutes(5)) return Result<CustomerSaleDto>.Validation("Sale date cannot be in the future");
        var incomeCategoryId = request.IncomeCategoryId ?? await GetOrCreateIncomeCategoryAsync(farmId);
        var paymentMethodId = request.PaymentMethodId ?? await GetOrCreatePaymentMethodAsync(farmId);
        var userId = _currentUser.GetUserId();
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var movement = new StockMovement { FarmId = farmId, InventoryItemId = item.Id, MovementType = InventoryMovementType.Consumption, Quantity = request.Quantity, MovementDate = date, Reason = $"Customer sale to {customer.Name}", PerformedBy = userId, CreatedBy = userId };
        item.Quantity -= request.Quantity; _context.StockMovements.Add(movement);
        var income = new IncomeRecord { FarmId = farmId, IncomeDate = date, Amount = Math.Round(request.TotalAmount, 2), IncomeCategoryId = incomeCategoryId, PaymentMethodId = paymentMethodId, Description = $"Sale to {customer.Name}: {item.Name}", CreatedBy = userId };
        _context.IncomeRecords.Add(income);
        var sale = new CustomerSale { FarmId = farmId, CustomerId = customer.Id, InventoryItemId = item.Id, Quantity = request.Quantity, TotalAmount = request.TotalAmount, SaleDate = date, StockMovement = movement, IncomeRecord = income, Notes = Clean(request.Notes), CreatedBy = userId };
        _context.CustomerSales.Add(sale);
        await _context.SaveChangesAsync(); await transaction.CommitAsync();
        return Result<CustomerSaleDto>.Success(ToDto(sale, customer, item));
    }

    private async Task<Guid> GetOrCreateIncomeCategoryAsync(Guid farmId)
    {
        var category = await _context.IncomeCategories.FirstOrDefaultAsync(c => c.FarmId == farmId && c.Name == "Inventory Sales");
        if (category != null) return category.Id;
        category = new IncomeCategory { FarmId = farmId, Name = "Inventory Sales", Description = "Income from inventory sales", CreatedBy = _currentUser.GetUserId() }; _context.IncomeCategories.Add(category); await _context.SaveChangesAsync(); return category.Id;
    }
    private async Task<Guid> GetOrCreatePaymentMethodAsync(Guid farmId)
    {
        var method = await _context.PaymentMethods.FirstOrDefaultAsync(m => m.FarmId == farmId && m.Name == "Other");
        if (method != null) return method.Id;
        method = new PaymentMethod { FarmId = farmId, Name = "Other", Description = "Other payment method", CreatedBy = _currentUser.GetUserId() }; _context.PaymentMethods.Add(method); await _context.SaveChangesAsync(); return method.Id;
    }
    private static string? ValidatePerson(string name, string? contact, string label) { if (string.IsNullOrWhiteSpace(name)) return $"{label} name is required"; if (name.Trim().Length > 200) return $"{label} name cannot exceed 200 characters"; if (contact?.Length > 1000) return "Contact information cannot exceed 1000 characters"; return null; }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static CustomerSaleDto ToDto(CustomerSale s) => ToDto(s, s.Customer, s.InventoryItem);
    private static CustomerSaleDto ToDto(CustomerSale s, Customer c, InventoryItem i) => new() { Id = s.Id, CustomerId = c.Id, CustomerName = c.Name, InventoryItemId = i.Id, InventoryItemName = i.Name, Unit = i.Unit, Quantity = s.Quantity, TotalAmount = s.TotalAmount, UnitPrice = s.Quantity == 0 ? 0 : s.TotalAmount / s.Quantity, SaleDate = s.SaleDate, StockMovementId = s.StockMovementId, IncomeRecordId = s.IncomeRecordId, Notes = s.Notes };
}
