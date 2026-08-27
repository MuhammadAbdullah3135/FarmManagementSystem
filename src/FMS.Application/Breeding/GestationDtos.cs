using FMS.Domain.Enums;

namespace FMS.Application.Breeding;

// Requests
public class ConfirmPregnancyRequest
{
    public Guid BreedingRecordId { get; set; }
    public DateTime ConfirmedDate { get; set; }
}

public class LogGestationHealthCheckRequest
{
    public DateTime CheckDate { get; set; }
    public string? Notes { get; set; }
    public string? PerformedBy { get; set; }
    public decimal? WeightKg { get; set; }
}

// DTOs
public class GestationRecordDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public Guid BreedingRecordId { get; set; }
    public Guid AnimalId { get; set; }
    public string AnimalTagNumber { get; set; } = string.Empty;
    public string? AnimalName { get; set; }
    public Guid SireId { get; set; }
    public string SireTagNumber { get; set; } = string.Empty;
    public DateTime BreedingDate { get; set; }
    public DateTime? ConfirmedDate { get; set; }
    public DateTime ExpectedDeliveryDate { get; set; }
    public int DaysUntilDue { get; set; }
    public int DaysElapsed { get; set; }
    public GestationStage CurrentStage { get; set; }
    public string? HealthCheckNotes { get; set; }
    public int HealthCheckCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GestationHealthCheckDto
{
    public Guid Id { get; set; }
    public Guid GestationRecordId { get; set; }
    public DateTime CheckDate { get; set; }
    public string? Notes { get; set; }
    public string? PerformedBy { get; set; }
    public decimal? WeightKg { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GestationRecordListFilter
{
    public GestationStage? Stage { get; set; }
    public bool ActiveOnly { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
