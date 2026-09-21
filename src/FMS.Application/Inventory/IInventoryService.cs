using FMS.Application.Common;

namespace FMS.Application.Inventory;

public interface IInventoryService
{
    Task<Result<PagedResult<InventoryItemDto>>> GetItemsAsync(Guid farmId, int page, int pageSize, string? search);
    Task<Result<InventoryItemDto>> GetItemByIdAsync(Guid farmId, Guid id);
    Task<Result<InventoryItemDto>> CreateItemAsync(Guid farmId, CreateInventoryItemRequest request);

    /// <summary>
    /// Validates a batch without writing it. Same rules and same duplicate check as
    /// <see cref="CreateItemAsync"/>, reported per row, so a bulk caller can preview a
    /// file and get exactly the answer a commit would give.
    /// </summary>
    Task<Result<BulkCreateResultDto>> ValidateItemsAsync(Guid farmId, IReadOnlyList<CreateInventoryItemRequest> requests);

    /// <summary>Creates the whole batch in one SaveChanges, so it is atomic.</summary>
    Task<Result<BulkCreateResultDto>> CreateItemsAsync(Guid farmId, IReadOnlyList<CreateInventoryItemRequest> requests);
    Task<Result<InventoryItemDto>> UpdateItemAsync(Guid farmId, Guid id, UpdateInventoryItemRequest request);
    Task<Result> DeleteItemAsync(Guid farmId, Guid id);
    Task<Result<StockMovementDto>> RecordMovementAsync(Guid farmId, RecordStockMovementRequest request);
    Task<Result<PagedResult<StockMovementDto>>> GetMovementsAsync(Guid farmId, Guid? inventoryItemId, string? movementType, DateTime? from, DateTime? to, int page, int pageSize);
    Task<Result<InventoryReportDto>> GetReportAsync(Guid farmId);
}
