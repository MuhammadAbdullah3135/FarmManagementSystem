using FMS.Application.Common;
using FMS.Application.Inventory;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Inventory;

public class InventoryService : IInventoryService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public InventoryService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<InventoryItemDto>>> GetItemsAsync(Guid farmId, int page, int pageSize, string? search)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;
        var query = _context.InventoryItems.AsNoTracking().Where(i => i.FarmId == farmId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i => i.Name.Contains(term) || (i.Category != null && i.Category.Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var items = await query.OrderBy(i => i.Name)
            .ThenBy(i => i.Category)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => ToDto(i))
            .ToListAsync();

        return Result<PagedResult<InventoryItemDto>>.Success(new PagedResult<InventoryItemDto>
        {
            Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount
        });
    }

    public async Task<Result<InventoryItemDto>> GetItemByIdAsync(Guid farmId, Guid id)
    {
        var item = await _context.InventoryItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.FarmId == farmId && i.Id == id);
        return item == null
            ? Result<InventoryItemDto>.NotFound("Inventory item not found")
            : Result<InventoryItemDto>.Success(ToDto(item));
    }

    public async Task<Result<InventoryItemDto>> CreateItemAsync(Guid farmId, CreateInventoryItemRequest request)
    {
        var validation = Validate(request.Name, request.Unit, request.Quantity, request.ReorderLevel, request.UnitCost);
        if (validation != null) return Result<InventoryItemDto>.Validation(validation);

        var name = request.Name.Trim();
        if (await _context.InventoryItems.AnyAsync(i => i.FarmId == farmId && i.Name == name))
            return Result<InventoryItemDto>.Conflict("An inventory item with this name already exists");

        var userId = _currentUser.GetUserId();
        var item = new InventoryItem
        {
            FarmId = farmId, Name = name, Category = Clean(request.Category), Unit = request.Unit.Trim(),
            Quantity = 0, ReorderLevel = request.ReorderLevel, UnitCost = request.UnitCost,
            Location = Clean(request.Location), CreatedBy = userId
        };
        _context.InventoryItems.Add(item);
        if (request.Quantity > 0)
        {
            item.StockMovements.Add(new StockMovement
            {
                FarmId = farmId, MovementType = InventoryMovementType.Purchase, Quantity = request.Quantity,
                MovementDate = DateTime.UtcNow, Reason = "Opening stock", PerformedBy = userId, CreatedBy = userId
            });
            item.Quantity = request.Quantity;
        }
        await _context.SaveChangesAsync();
        return Result<InventoryItemDto>.Success(ToDto(item));
    }

    public async Task<Result<InventoryItemDto>> UpdateItemAsync(Guid farmId, Guid id, UpdateInventoryItemRequest request)
    {
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.FarmId == farmId && i.Id == id);
        if (item == null) return Result<InventoryItemDto>.NotFound("Inventory item not found");

        var validation = Validate(request.Name, request.Unit, 0, request.ReorderLevel, request.UnitCost, false);
        if (validation != null) return Result<InventoryItemDto>.Validation(validation);
        var name = request.Name.Trim();
        if (await _context.InventoryItems.AnyAsync(i => i.FarmId == farmId && i.Id != id && i.Name == name))
            return Result<InventoryItemDto>.Conflict("An inventory item with this name already exists");

        item.Name = name;
        item.Category = Clean(request.Category);
        item.Unit = request.Unit.Trim();
        item.ReorderLevel = request.ReorderLevel;
        item.UnitCost = request.UnitCost;
        item.Location = Clean(request.Location);
        item.ModifiedAt = DateTime.UtcNow;
        item.ModifiedBy = _currentUser.GetUserId();
        await _context.SaveChangesAsync();
        return Result<InventoryItemDto>.Success(ToDto(item));
    }

    public async Task<Result> DeleteItemAsync(Guid farmId, Guid id)
    {
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.FarmId == farmId && i.Id == id);
        if (item == null) return Result.NotFound("Inventory item not found");
        if (await _context.StockMovements.AnyAsync(m => m.InventoryItemId == id))
            return Result.Conflict("Cannot delete an inventory item with stock movement history");
        _context.InventoryItems.Remove(item);
        await _context.SaveChangesAsync();
        return Result.Success();
    }

    private static string? Validate(string name, string unit, decimal quantity, decimal reorderLevel, decimal unitCost, bool validateQuantity = true)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Item name is required";
        if (name.Trim().Length > 200) return "Item name cannot exceed 200 characters";
        if (string.IsNullOrWhiteSpace(unit)) return "Unit is required";
        if (unit.Trim().Length > 50) return "Unit cannot exceed 50 characters";
        if (validateQuantity && quantity < 0) return "Quantity cannot be negative";
        if (reorderLevel < 0) return "Reorder level cannot be negative";
        if (unitCost < 0) return "Unit cost cannot be negative";
        return null;
    }

    public async Task<Result<StockMovementDto>> RecordMovementAsync(Guid farmId, RecordStockMovementRequest request)
    {
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.FarmId == farmId && i.Id == request.InventoryItemId);
        if (item == null) return Result<StockMovementDto>.NotFound("Inventory item not found");
        if (!Enum.IsDefined(request.MovementType)) return Result<StockMovementDto>.Validation("Invalid movement type");

        var signedQuantity = request.MovementType switch
        {
            InventoryMovementType.Purchase => request.Quantity,
            InventoryMovementType.Consumption => -request.Quantity,
            InventoryMovementType.Transfer or InventoryMovementType.Adjustment => request.Quantity,
            _ => 0
        };
        if (request.MovementType == InventoryMovementType.Consumption && request.Quantity <= 0)
            return Result<StockMovementDto>.Validation("Consumption quantity must be greater than zero");
        if (request.MovementType == InventoryMovementType.Purchase && request.Quantity <= 0)
            return Result<StockMovementDto>.Validation("Purchase quantity must be greater than zero");
        if (request.MovementType is InventoryMovementType.Transfer or InventoryMovementType.Adjustment && request.Quantity == 0)
            return Result<StockMovementDto>.Validation("Transfer or adjustment quantity cannot be zero");
        if (item.Quantity + signedQuantity < 0)
            return Result<StockMovementDto>.Conflict($"Insufficient stock: available quantity is {item.Quantity} {item.Unit}");

        var date = request.MovementDate ?? DateTime.UtcNow;
        if (date > DateTime.UtcNow.AddMinutes(5))
            return Result<StockMovementDto>.Validation("Movement date cannot be in the future");
        var userId = _currentUser.GetUserId();
        var movement = new StockMovement
        {
            FarmId = farmId, InventoryItemId = item.Id, MovementType = request.MovementType,
            Quantity = request.Quantity, MovementDate = date, Reason = Clean(request.Reason),
            PerformedBy = userId, CreatedBy = userId
        };
        item.Quantity += signedQuantity;
        _context.StockMovements.Add(movement);
        await _context.SaveChangesAsync();
        return Result<StockMovementDto>.Success(ToMovementDto(movement, item));
    }

    public async Task<Result<PagedResult<StockMovementDto>>> GetMovementsAsync(Guid farmId, Guid? inventoryItemId, string? movementType, DateTime? from, DateTime? to, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;
        var query = _context.StockMovements.AsNoTracking().Include(m => m.InventoryItem).Where(m => m.FarmId == farmId);
        if (inventoryItemId.HasValue) query = query.Where(m => m.InventoryItemId == inventoryItemId.Value);
        if (!string.IsNullOrWhiteSpace(movementType))
        {
            if (!Enum.TryParse<InventoryMovementType>(movementType, true, out var parsed))
                return Result<PagedResult<StockMovementDto>>.Validation("Movement type must be Purchase, Consumption, Transfer, or Adjustment");
            query = query.Where(m => m.MovementType == parsed);
        }
        if (from.HasValue) query = query.Where(m => m.MovementDate >= from.Value);
        if (to.HasValue) query = query.Where(m => m.MovementDate < to.Value.Date.AddDays(1));
        var totalCount = await query.CountAsync();
        var movements = await query.OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result<PagedResult<StockMovementDto>>.Success(new PagedResult<StockMovementDto>
        {
            Items = movements.Select(m => ToMovementDto(m, m.InventoryItem)).ToList(), Page = page, PageSize = pageSize, TotalCount = totalCount
        });
    }

    public async Task<Result<InventoryReportDto>> GetReportAsync(Guid farmId)
    {
        var items = await _context.InventoryItems.AsNoTracking().Where(i => i.FarmId == farmId).OrderBy(i => i.Name).ToListAsync();
        var movements = await _context.StockMovements.AsNoTracking().Where(m => m.FarmId == farmId).ToListAsync();
        var currentStock = items.Select(i => new InventoryStockReportItemDto
        {
            InventoryItemId = i.Id, ItemName = i.Name, Category = i.Category, Unit = i.Unit, Quantity = i.Quantity,
            ReorderLevel = i.ReorderLevel, UnitCost = i.UnitCost, StockValue = i.Quantity * i.UnitCost,
            Location = i.Location, IsLowStock = i.Quantity <= i.ReorderLevel
        }).ToList();
        var summary = movements.GroupBy(m => m.MovementType).Select(g => new InventoryMovementSummaryDto
        {
            MovementType = g.Key, MovementTypeName = g.Key.ToString(), MovementCount = g.Count(), Quantity = g.Sum(m => Math.Abs(m.Quantity))
        }).OrderBy(s => s.MovementType).ToList();
        var alerts = currentStock.Where(i => i.IsLowStock).Select(i => new InventoryLowStockAlertDto
        {
            InventoryItemId = i.InventoryItemId, ItemName = i.ItemName, Unit = i.Unit, Quantity = i.Quantity,
            ReorderLevel = i.ReorderLevel, Shortfall = Math.Max(0, i.ReorderLevel - i.Quantity), Location = i.Location
        }).OrderByDescending(a => a.Shortfall).ThenBy(a => a.ItemName).ToList();
        return Result<InventoryReportDto>.Success(new InventoryReportDto
        {
            TotalStockValue = currentStock.Sum(i => i.StockValue), TotalItems = currentStock.Count,
            LowStockItemCount = alerts.Count, TotalMovementQuantity = movements.Sum(m => Math.Abs(m.Quantity)),
            CurrentStock = currentStock, MovementSummary = summary, LowStockAlerts = alerts
        });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StockMovementDto ToMovementDto(StockMovement movement, InventoryItem item) => new()
    {
        Id = movement.Id, FarmId = movement.FarmId, InventoryItemId = item.Id, InventoryItemName = item.Name,
        Unit = item.Unit, MovementType = movement.MovementType, MovementTypeName = movement.MovementType.ToString(),
        Quantity = movement.Quantity, SignedQuantity = movement.MovementType == InventoryMovementType.Consumption ? -movement.Quantity : movement.Quantity,
        MovementDate = movement.MovementDate, Reason = movement.Reason, PerformedBy = movement.PerformedBy
    };

    private static InventoryItemDto ToDto(InventoryItem item) => new()
    {
        Id = item.Id, FarmId = item.FarmId, Name = item.Name, Category = item.Category, Unit = item.Unit,
        Quantity = item.Quantity, ReorderLevel = item.ReorderLevel, UnitCost = item.UnitCost,
        Location = item.Location, CreatedAt = item.CreatedAt
    };
}
