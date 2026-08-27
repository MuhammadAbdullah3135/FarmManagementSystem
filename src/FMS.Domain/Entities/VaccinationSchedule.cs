using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class VaccinationSchedule : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid VaccineTypeId { get; set; }
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public int RecurrenceDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public VaccineType VaccineType { get; set; } = null!;
    public AnimalType? AnimalType { get; set; }
    public Breed? Breed { get; set; }
}
