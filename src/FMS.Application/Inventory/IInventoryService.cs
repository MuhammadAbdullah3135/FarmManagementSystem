using FMS.Application.Common;

namespace FMS.Application.Inventory;

public interface IInventoryService
{
    Task<Result<PagedResult<InventoryItemDto>>> GetItemsAsync(Guid farmId, int page, int pageSize, string? search);
    Task<Result<InventoryItemDto>> GetItemByIdAsync(Guid farmId, Guid id);
    Task<Result<InventoryItemDto>> CreateItemAsync(Guid farmId, CreateInventoryItemRequest request);
    Task<Result<InventoryItemDto>> UpdateItemAsync(Guid farmId, Guid id, UpdateInventoryItemRequest request);
    Task<Result> DeleteItemAsync(Guid farmId, Guid id);
    Task<Result<StockMovementDto>> RecordMovementAsync(Guid farmId, RecordStockMovementRequest request);
    Task<Result<PagedResult<StockMovementDto>>> GetMovementsAsync(Guid farmId, Guid? inventoryItemId, string? movementType, DateTime? from, DateTime? to, int page, int pageSize);
    Task<Result<InventoryReportDto>> GetReportAsync(Guid farmId);
}
