using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class MedicalRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid AnimalId { get; set; }
    public string Symptoms { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? Treatment { get; set; }
    public string? MedicineUsed { get; set; }
    public string? Dosage { get; set; }
    public string? VetName { get; set; }
    public decimal Cost { get; set; }
    public DateTime DateRecorded { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public MedicalRecordStatus Status { get; set; } = MedicalRecordStatus.Open;
    public string? Notes { get; set; }
    public Guid? FollowUpTaskId { get; set; }
    public Guid? ExpenseId { get; set; }

    public Farm Farm { get; set; } = null!;
    public Animal Animal { get; set; } = null!;
}
