using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

/// <summary>
/// A concrete daily feeding task generated from an active feeding schedule.
/// </summary>
public class FeedingTask : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid FeedingScheduleId { get; set; }
    public Guid DietPlanId { get; set; }
    public DateTime TaskDate { get; set; }
    public TimeSpan TimeOfDay { get; set; }
    public FeedingTaskStatus Status { get; set; } = FeedingTaskStatus.Pending;

    /// <summary>Animals matching the diet plan criteria at generation time.</summary>
    public int TargetAnimalCount { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public string? Notes { get; set; }

    public FeedingSchedule FeedingSchedule { get; set; } = null!;
    public DietPlan DietPlan { get; set; } = null!;
}
