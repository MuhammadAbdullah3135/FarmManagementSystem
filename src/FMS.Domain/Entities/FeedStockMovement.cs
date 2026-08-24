using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class FeedStockMovement : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid FeedTypeId { get; set; }
    public StockMovementType MovementType { get; set; }

    /// <summary>
    /// Always stored as a positive magnitude. Direction is derived from MovementType
    /// (Purchase adds, Consumption subtracts, Adjustment uses SignedQuantity).
    /// </summary>
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public string? Supplier { get; set; }
    public string? Notes { get; set; }
    public DateTime MovementDate { get; set; }

    public FeedType FeedType { get; set; } = null!;
}
