namespace FMS.Application.Health;

// ── WeightCheckSchedule DTOs ────────────────────────────

public class CreateWeightCheckScheduleRequest
{
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public int RecurrenceDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public class UpdateWeightCheckScheduleRequest
{
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public int RecurrenceDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public class WeightCheckScheduleDto
{
    public Guid Id { get; set; }
    public Guid? AnimalTypeId { get; set; }
    public string? AnimalTypeName { get; set; }
    public Guid? BreedId { get; set; }
    public string? BreedName { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public string? AgeCategoryName { get; set; }
    public int RecurrenceDays { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── WeightCheckStatus DTOs ──────────────────────────────

public enum WeightCheckStatusType
{
    Upcoming,
    Due,
    Overdue
}

public class WeightCheckStatusDto
{
    public Guid AnimalId { get; set; }
    public string AnimalTagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public DateTime? LastWeightDate { get; set; }
    public DateTime NextDueDate { get; set; }
    public WeightCheckStatusType Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int DaysUntilDue { get; set; }
}
