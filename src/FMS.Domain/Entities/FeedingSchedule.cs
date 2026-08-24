using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class FeedingSchedule : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid DietPlanId { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public string? Label { get; set; }
    public bool IsActive { get; set; } = true;

    public DietPlan DietPlan { get; set; } = null!;
}
