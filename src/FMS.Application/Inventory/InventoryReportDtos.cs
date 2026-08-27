using FMS.Domain.Enums;

namespace FMS.Application.Inventory;

public class InventoryReportDto
{
    public decimal TotalStockValue { get; set; }
    public int TotalItems { get; set; }
    public int LowStockItemCount { get; set; }
    public decimal TotalMovementQuantity { get; set; }
    public List<InventoryStockReportItemDto> CurrentStock { get; set; } = new();
    public List<InventoryMovementSummaryDto> MovementSummary { get; set; } = new();
    public List<InventoryLowStockAlertDto> LowStockAlerts { get; set; } = new();
}

public class InventoryStockReportItemDto
{
    public Guid InventoryItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal UnitCost { get; set; }
    public decimal StockValue { get; set; }
    public string? Location { get; set; }
    public bool IsLowStock { get; set; }
}

public class InventoryMovementSummaryDto
{
    public InventoryMovementType MovementType { get; set; }
    public string MovementTypeName { get; set; } = string.Empty;
    public int MovementCount { get; set; }
    public decimal Quantity { get; set; }
}

public class InventoryLowStockAlertDto
{
    public Guid InventoryItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal Shortfall { get; set; }
    public string? Location { get; set; }
}
