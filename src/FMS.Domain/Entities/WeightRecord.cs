using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class WeightRecord : AuditableEntity
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public decimal WeightKg { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? Notes { get; set; }

    public Animal Animal { get; set; } = null!;
}
