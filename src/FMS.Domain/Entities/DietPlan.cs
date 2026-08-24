using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// A diet plan targets a segment of animals (by type, breed, age category and/or
/// weight range). Null criteria mean "any". Items define what to feed per scheduled feeding.
/// </summary>
public class DietPlan : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public decimal? MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public AnimalType? AnimalType { get; set; }
    public Breed? Breed { get; set; }
    public AgeCategory? AgeCategory { get; set; }
    public ICollection<DietPlanItem> Items { get; set; } = new List<DietPlanItem>();
    public ICollection<FeedingSchedule> Schedules { get; set; } = new List<FeedingSchedule>();
}
