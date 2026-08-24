using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class AnimalStatus : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public AnimalStatusCategory Category { get; set; } = AnimalStatusCategory.Active;

    public Farm Farm { get; set; } = null!;
    public ICollection<Animal> Animals { get; set; } = new List<Animal>();
}
