using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class FeedType : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FeedCategory Category { get; set; }
    public FeedUnit Unit { get; set; }
    public decimal CostPerUnit { get; set; }
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<FeedStockMovement> StockMovements { get; set; } = new List<FeedStockMovement>();
}
