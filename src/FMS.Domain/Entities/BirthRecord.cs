using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class BirthRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid DamId { get; set; }
    public Guid? GestationRecordId { get; set; }
    public Guid? BreedingRecordId { get; set; }
    public DateTime BirthDate { get; set; }
    public int OffspringCount { get; set; }
    public string? Notes { get; set; }
    public string? VetName { get; set; }

    public Farm Farm { get; set; } = null!;
    public Animal Dam { get; set; } = null!;
    public GestationRecord? GestationRecord { get; set; }
    public BreedingRecord? BreedingRecord { get; set; }
    public ICollection<BirthOffspring> Offspring { get; set; } = new List<BirthOffspring>();
}
