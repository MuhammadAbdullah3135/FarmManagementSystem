using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class BirthOffspring : BaseEntity
{
    public Guid FarmId { get; set; }
    public Guid BirthRecordId { get; set; }
    public Guid? OffspringAnimalId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid SexOptionId { get; set; }
    public BirthOutcome Outcome { get; set; }
    public decimal? BirthWeightKg { get; set; }
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public BirthRecord BirthRecord { get; set; } = null!;
    public Animal? OffspringAnimal { get; set; }
    public SexOption SexOption { get; set; } = null!;
}
