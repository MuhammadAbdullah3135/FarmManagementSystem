using FMS.Domain.Common;
using FMS.Domain.Enums;

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

    /// <summary>
    /// Which feeding of the day this was. Null for records that predate the concept: the
    /// slot is not inferred from <see cref="FedAt"/> after the fact, so a record never
    /// claims a feeding the person who recorded it never chose.
    /// </summary>
    public MealSlot? MealSlot { get; set; }

    /// <summary>Whether this was the main ration or something offered alongside it.</summary>
    public MealRole? MealRole { get; set; }

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
