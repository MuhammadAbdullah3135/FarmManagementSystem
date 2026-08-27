using FMS.Domain.Enums;

namespace FMS.Application.Inventory;

public class InventoryItemDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal UnitCost { get; set; }
    public string? Location { get; set; }
    public decimal StockValue => Quantity * UnitCost;
    public bool IsLowStock => Quantity <= ReorderLevel;
    public DateTime CreatedAt { get; set; }
}

public class CreateInventoryItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReorderLevel { get; set; }
    public decimal UnitCost { get; set; }
    public string? Location { get; set; }
}

public class RecordStockMovementRequest
{
    public Guid InventoryItemId { get; set; }
    public InventoryMovementType MovementType { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? MovementDate { get; set; }
    public string? Reason { get; set; }
}

public class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public Guid InventoryItemId { get; set; }
    public string InventoryItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public InventoryMovementType MovementType { get; set; }
    public string MovementTypeName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SignedQuantity { get; set; }
    public DateTime MovementDate { get; set; }
    public string? Reason { get; set; }
    public Guid? PerformedBy { get; set; }
}

public class UpdateInventoryItemRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal ReorderLevel { get; set; }
    public decimal UnitCost { get; set; }
    public string? Location { get; set; }
}
