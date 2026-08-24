using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class SexOption : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Value { get; set; } = string.Empty;

    public Farm Farm { get; set; } = null!;
    public ICollection<Animal> Animals { get; set; } = new List<Animal>();
}
