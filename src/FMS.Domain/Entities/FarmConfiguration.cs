using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class FarmConfiguration : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Category { get; set; }

    public Farm Farm { get; set; } = null!;
}
