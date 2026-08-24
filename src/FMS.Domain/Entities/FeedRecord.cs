using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class FeedRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid FeedTypeId { get; set; }

    /// <summary>Set for individual feeding. Exactly one of AnimalId / LocationId is required.</summary>
    public Guid? AnimalId { get; set; }

    /// <summary>Set for group feeding by location. Exactly one of AnimalId / LocationId is required.</summary>
    public Guid? LocationId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Snapshot of FeedType.CostPerUnit at recording time, for stable cost reporting.</summary>
    public decimal UnitCost { get; set; }
    public DateTime FedAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>Consumption stock movement generated for this record.</summary>
    public Guid? StockMovementId { get; set; }

    public FeedType FeedType { get; set; } = null!;
    public Animal? Animal { get; set; }
    public Location? Location { get; set; }
}
