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
        // One item, the endpoint's own path: the rules come from InventoryItemRules,
        // shared with the bulk import, and the answer is still the first message.
        var errors = InventoryItemRules.Validate(
            request.Name, request.Category, request.Unit, request.Quantity,
            request.ReorderLevel, request.UnitCost, request.Location);
        if (errors.Count > 0) return Result<InventoryItemDto>.Validation(errors[0].Message, errors[0].MessageKey, errors[0].MessageArgs);

        var name = request.Name.Trim();
        if (await _context.InventoryItems.AnyAsync(i => i.FarmId == farmId && i.Name == name))
            return Result<InventoryItemDto>.Conflict(InventoryItemRules.DuplicateNameMessage);

        var item = InventoryItemRules.Build(farmId, request, _currentUser.GetUserId());
        _context.InventoryItems.Add(item);
        await _context.SaveChangesAsync();
        return Result<InventoryItemDto>.Success(ToDto(item));
    }

    /// <summary>
    /// Validates a batch without writing it, using the same rules and the same
    /// duplicate check as <see cref="CreateItemAsync"/>, so what this predicts is what
    /// <see cref="CreateItemsAsync"/> does.
    /// </summary>
    public Task<Result<BulkCreateResultDto>> ValidateItemsAsync(
        Guid farmId, IReadOnlyList<CreateInventoryItemRequest> requests) =>
        CheckBatchAsync(farmId, requests);

    /// <summary>
    /// Creates every item in a single SaveChanges, which EF wraps in one transaction —
    /// so a batch is atomic: either every item is written or none is. That is what the
    /// import's all-or-nothing promise rests on, on PostgreSQL and on the InMemory test
    /// provider alike.
    /// </summary>
    public async Task<Result<BulkCreateResultDto>> CreateItemsAsync(
        Guid farmId, IReadOnlyList<CreateInventoryItemRequest> requests)
    {
        var checkedBatch = await CheckBatchAsync(farmId, requests);
        if (!checkedBatch.IsSuccess)
            return checkedBatch;

        if (checkedBatch.Value!.Failures.Count > 0)
            return checkedBatch;

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        foreach (var request in requests)
            _context.InventoryItems.Add(InventoryItemRules.Build(farmId, request, userId, now));

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
    /// Names already in the farm are loaded in one query rather than one per row, and
    /// the batch's own names are compared against each other too — without that second
    /// check a file naming the same item twice would reach SaveChanges and fail the
    /// whole batch with a database error instead of two row-level messages.
    /// </summary>
    private async Task<Result<BulkCreateResultDto>> CheckBatchAsync(
        Guid farmId, IReadOnlyList<CreateInventoryItemRequest> requests)
    {
        var failures = new List<BulkCreateFailureDto>();
        var names = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.Name))
            .Select(request => request.Name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Case-sensitive, matching the endpoint's `i.Name == name` and the index itself.
        var existing = names.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _context.InventoryItems
                .Where(item => item.FarmId == farmId && names.Contains(item.Name))
                .Select(item => item.Name)
                .ToListAsync())
                .ToHashSet(StringComparer.Ordinal);

        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var errors = InventoryItemRules.Validate(
                request.Name, request.Category, request.Unit, request.Quantity,
                request.ReorderLevel, request.UnitCost, request.Location);

            if (errors.Count > 0)
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = errors[0].Message });
                continue;
            }

            var name = request.Name.Trim();

            if (existing.Contains(name))
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = InventoryItemRules.DuplicateNameMessage });
                continue;
            }

            if (!seenInBatch.Add(name))
            {
                failures.Add(new BulkCreateFailureDto
                    { Index = index, Message = $"The batch contains the same item name '{name}' twice" });
            }
        }

        return Result<BulkCreateResultDto>.Success(new BulkCreateResultDto
        {
            RequestedCount = requests.Count,
            SuccessCount = requests.Count - failures.Count,
            Failures = failures
        });
    }

    public async Task<Result<InventoryItemDto>> UpdateItemAsync(Guid farmId, Guid id, UpdateInventoryItemRequest request)
    {
        var item = await _context.InventoryItems.FirstOrDefaultAsync(i => i.FarmId == farmId && i.Id == id);
        if (item == null) return Result<InventoryItemDto>.NotFound("Inventory item not found");

        var error = InventoryItemRules.Validate(
            request.Name, request.Category, request.Unit, 0,
            request.ReorderLevel, request.UnitCost, request.Location, validateQuantity: false);
        if (error.Count > 0) return Result<InventoryItemDto>.Validation(error[0].Message, error[0].MessageKey, error[0].MessageArgs);
        var name = request.Name.Trim();
        if (await _context.InventoryItems.AnyAsync(i => i.FarmId == farmId && i.Id != id && i.Name == name))
            return Result<InventoryItemDto>.Conflict(InventoryItemRules.DuplicateNameMessage);

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
