using FMS.Domain.Enums;

namespace FMS.Application.Breeding;

// Requests
public class CreateBirthRecordRequest
{
    public Guid DamId { get; set; }
    public Guid? GestationRecordId { get; set; }
    public Guid? BreedingRecordId { get; set; }
    public DateTime BirthDate { get; set; }
    public string? VetName { get; set; }
    public string? Notes { get; set; }
    public List<CreateBirthOffspringRequest> Offspring { get; set; } = new();
}

public class CreateBirthOffspringRequest
{
    public Guid SexOptionId { get; set; }
    public BirthOutcome Outcome { get; set; } = BirthOutcome.Alive;
    public decimal? BirthWeightKg { get; set; }
    public string? Name { get; set; }
    public string? Notes { get; set; }
}

// DTOs
public class BirthRecordDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public Guid DamId { get; set; }
    public string DamTagNumber { get; set; } = string.Empty;
    public string? DamName { get; set; }
    public Guid? GestationRecordId { get; set; }
    public Guid? BreedingRecordId { get; set; }
    public DateTime BirthDate { get; set; }
    public int OffspringCount { get; set; }
    public int AliveCount { get; set; }
    public string? VetName { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<BirthOffspringDto> Offspring { get; set; } = new();
}

public class BirthOffspringDto
{
    public Guid Id { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid SexOptionId { get; set; }
    public string SexValue { get; set; } = string.Empty;
    public BirthOutcome Outcome { get; set; }
    public decimal? BirthWeightKg { get; set; }
    public Guid? OffspringAnimalId { get; set; }
    public string? Notes { get; set; }
}

public class BirthRecordListFilter
{
    public Guid? DamId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
