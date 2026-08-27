using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class VaccinationRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid AnimalId { get; set; }
    public Guid VaccineTypeId { get; set; }
    public DateTime DateGiven { get; set; }
    public string? VetName { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
    public Guid? ExpenseId { get; set; }

    public Farm Farm { get; set; } = null!;
    public Animal Animal { get; set; } = null!;
    public VaccineType VaccineType { get; set; } = null!;
}
