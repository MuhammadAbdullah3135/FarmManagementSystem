using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class VaccineType : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DefaultDosage { get; set; }
    public string? Notes { get; set; }
    public Guid? LinkedMedicineId { get; set; }

    public Farm Farm { get; set; } = null!;
    public Medicine? LinkedMedicine { get; set; }
    public ICollection<VaccinationRecord> VaccinationRecords { get; set; } = new List<VaccinationRecord>();
    public ICollection<VaccinationSchedule> VaccinationSchedules { get; set; } = new List<VaccinationSchedule>();
}
