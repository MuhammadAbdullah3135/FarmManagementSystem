using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class AnimalIdentification : BaseEntity
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public Guid IdentificationTypeId { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTime? DateAttached { get; set; }
    public DateTime? DateRemoved { get; set; }

    public Animal Animal { get; set; } = null!;
    public IdentificationType IdentificationType { get; set; } = null!;
}
