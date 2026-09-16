namespace FMS.Application.Health;

// ── VaccineType DTOs ────────────────────────────────────

public class CreateVaccineTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? DefaultDosage { get; set; }
    public string? Notes { get; set; }
    public Guid? LinkedMedicineId { get; set; }
}

public class UpdateVaccineTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? DefaultDosage { get; set; }
    public string? Notes { get; set; }
    public Guid? LinkedMedicineId { get; set; }
}

public class VaccineTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DefaultDosage { get; set; }
    public string? Notes { get; set; }
    public Guid? LinkedMedicineId { get; set; }
    public string? LinkedMedicineName { get; set; }
    public int VaccinationCount { get; set; }
    public int ScheduleCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VaccineTypeListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DefaultDosage { get; set; }
    public Guid? LinkedMedicineId { get; set; }
    public string? LinkedMedicineName { get; set; }
    public int VaccinationCount { get; set; }
    public int ScheduleCount { get; set; }
}

// ── VaccinationRecord DTOs ──────────────────────────────

public class CreateVaccinationRecordRequest
{
    public Guid AnimalId { get; set; }
    public Guid VaccineTypeId { get; set; }
    public DateTime DateGiven { get; set; }
    public string? VetName { get; set; }
    public string? BatchNumber { get; set; }
    /// <summary>
    /// Units of the linked medicine this vaccination consumes.
    /// Defaults to 1 when omitted (backwards compatible with existing clients).
    /// </summary>
    public int? QuantityUsed { get; set; }
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
}

public class UpdateVaccinationRecordRequest
{
    public Guid AnimalId { get; set; }
    public Guid VaccineTypeId { get; set; }
    public DateTime DateGiven { get; set; }
    public string? VetName { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
}

public class VaccinationRecordListFilter
{
    public string? Search { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? VaccineTypeId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class VaccinationRecordDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public Guid AnimalId { get; set; }
    public string AnimalTagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public Guid VaccineTypeId { get; set; }
    public string VaccineTypeName { get; set; } = string.Empty;
    public DateTime DateGiven { get; set; }
    public string? VetName { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VaccinationRecordListItemDto
{
    public Guid Id { get; set; }
    public Guid AnimalId { get; set; }
    public string AnimalTagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public Guid VaccineTypeId { get; set; }
    public string VaccineTypeName { get; set; } = string.Empty;
    public DateTime DateGiven { get; set; }
    public string? VetName { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Cost { get; set; }
}

// ── VaccinationSchedule DTOs ────────────────────────────

public class CreateVaccinationScheduleRequest
{
    public Guid VaccineTypeId { get; set; }
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public int RecurrenceDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public class UpdateVaccinationScheduleRequest
{
    public Guid VaccineTypeId { get; set; }
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public int RecurrenceDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public class VaccinationScheduleDto
{
    public Guid Id { get; set; }
    public Guid VaccineTypeId { get; set; }
    public string VaccineTypeName { get; set; } = string.Empty;
    public Guid? AnimalTypeId { get; set; }
    public string? AnimalTypeName { get; set; }
    public Guid? BreedId { get; set; }
    public string? BreedName { get; set; }
    public int RecurrenceDays { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── VaccinationStatus DTOs ──────────────────────────────

public enum VaccinationStatusType
{
    Upcoming,
    Due,
    Overdue
}

public class VaccinationStatusDto
{
    public Guid AnimalId { get; set; }
    public string AnimalTagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public Guid VaccineTypeId { get; set; }
    public string VaccineTypeName { get; set; } = string.Empty;
    public DateTime? LastVaccinationDate { get; set; }
    public DateTime NextDueDate { get; set; }
    public VaccinationStatusType Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int DaysUntilDue { get; set; }
}
