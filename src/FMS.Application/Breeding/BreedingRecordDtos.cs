using FMS.Domain.Enums;

namespace FMS.Application.Breeding;

// Requests
public class CreateBreedingRecordRequest
{
    public Guid SireId { get; set; }
    public Guid DamId { get; set; }
    public DateTime BreedingDate { get; set; }
    public BreedingMethod Method { get; set; }
    public string? VetName { get; set; }
    public string? Notes { get; set; }
}

public class UpdateBreedingRecordRequest
{
    public Guid SireId { get; set; }
    public Guid DamId { get; set; }
    public DateTime BreedingDate { get; set; }
    public BreedingMethod Method { get; set; }
    public string? VetName { get; set; }
    public BreedingResult Result { get; set; }
    public string? Notes { get; set; }
}

public class BreedingRecordListFilter
{
    public Guid? SireId { get; set; }
    public Guid? DamId { get; set; }
    public BreedingResult? Result { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// DTOs
public class BreedingRecordDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public Guid SireId { get; set; }
    public string SireTagNumber { get; set; } = string.Empty;
    public string? SireName { get; set; }
    public Guid DamId { get; set; }
    public string DamTagNumber { get; set; } = string.Empty;
    public string? DamName { get; set; }
    public DateTime BreedingDate { get; set; }
    public BreedingMethod Method { get; set; }
    public string? VetName { get; set; }
    public BreedingResult Result { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
