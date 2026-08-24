using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class AnimalTransfer : AuditableEntity
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public Guid? FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }
    public DateTime TransferredAt { get; set; }
    public string? Reason { get; set; }

    public Animal Animal { get; set; } = null!;
    public Location? FromLocation { get; set; }
    public Location ToLocation { get; set; } = null!;
}
