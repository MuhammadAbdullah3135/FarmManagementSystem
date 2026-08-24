using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class AgeCategory : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MinDays { get; set; }
    public int MaxDays { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<Animal> Animals { get; set; } = new List<Animal>();
}
