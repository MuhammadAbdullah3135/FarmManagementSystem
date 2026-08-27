using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class MedicineUsage : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid MedicineId { get; set; }
    public Guid MedicineStockId { get; set; }
    public Guid? MedicalRecordId { get; set; }
    public int QuantityUsed { get; set; }
    public DateTime DateUsed { get; set; }
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public Medicine Medicine { get; set; } = null!;
    public MedicineStock MedicineStock { get; set; } = null!;
    public MedicalRecord? MedicalRecord { get; set; }
}
