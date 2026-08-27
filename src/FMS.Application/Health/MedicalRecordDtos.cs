using FMS.Domain.Enums;

namespace FMS.Application.Health;

public class CreateMedicalRecordRequest
{
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
}

public class UpdateMedicalRecordRequest
{
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
}

public class MedicalRecordListFilter
{
    public string? Search { get; set; }
    public Guid? AnimalId { get; set; }
    public MedicalRecordStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class MedicalRecordDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public Guid AnimalId { get; set; }
    public string AnimalTagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public string Symptoms { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? Treatment { get; set; }
    public string? MedicineUsed { get; set; }
    public string? Dosage { get; set; }
    public string? VetName { get; set; }
    public decimal Cost { get; set; }
    public DateTime DateRecorded { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public MedicalRecordStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid? FollowUpTaskId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MedicalRecordListItemDto
{
    public Guid Id { get; set; }
    public Guid AnimalId { get; set; }
    public string AnimalTagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public string? Diagnosis { get; set; }
    public string? VetName { get; set; }
    public decimal Cost { get; set; }
    public DateTime DateRecorded { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public MedicalRecordStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
}
