using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class IdentificationType : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;

    public Farm Farm { get; set; } = null!;
}
