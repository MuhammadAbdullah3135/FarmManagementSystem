using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class BreedingRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid SireId { get; set; }
    public Guid DamId { get; set; }
    public DateTime BreedingDate { get; set; }
    public BreedingMethod Method { get; set; }
    public string? VetName { get; set; }
    public BreedingResult Result { get; set; } = BreedingResult.Pending;
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public Animal Sire { get; set; } = null!;
    public Animal Dam { get; set; } = null!;
}
