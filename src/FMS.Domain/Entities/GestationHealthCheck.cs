using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class GestationHealthCheck : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid GestationRecordId { get; set; }
    public DateTime CheckDate { get; set; }
    public string? Notes { get; set; }
    public string? PerformedBy { get; set; }
    public decimal? WeightKg { get; set; }

    public Farm Farm { get; set; } = null!;
    public GestationRecord GestationRecord { get; set; } = null!;
}
