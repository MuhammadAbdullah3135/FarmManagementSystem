using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class GestationRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid BreedingRecordId { get; set; }
    public Guid AnimalId { get; set; }
    public DateTime? ConfirmedDate { get; set; }
    public DateTime ExpectedDeliveryDate { get; set; }
    public GestationStage CurrentStage { get; set; }
    public string? HealthCheckNotes { get; set; }

    public Farm Farm { get; set; } = null!;
    public BreedingRecord BreedingRecord { get; set; } = null!;
    public Animal Animal { get; set; } = null!;
    public ICollection<GestationHealthCheck> HealthChecks { get; set; } = new List<GestationHealthCheck>();
}
