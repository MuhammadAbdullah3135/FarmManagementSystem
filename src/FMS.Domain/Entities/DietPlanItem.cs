using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class DietPlanItem : AuditableEntity
{
    public Guid DietPlanId { get; set; }
    public Guid FeedTypeId { get; set; }
    public decimal QuantityPerFeeding { get; set; }

    public DietPlan DietPlan { get; set; } = null!;
    public FeedType FeedType { get; set; } = null!;
}
