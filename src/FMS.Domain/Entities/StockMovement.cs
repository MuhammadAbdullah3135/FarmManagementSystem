using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class StockMovement : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryMovementType MovementType { get; set; }
    public decimal Quantity { get; set; }
    public DateTime MovementDate { get; set; }
    public string? Reason { get; set; }
    public Guid? PerformedBy { get; set; }

    public Farm Farm { get; set; } = null!;
    public InventoryItem InventoryItem { get; set; } = null!;
}
