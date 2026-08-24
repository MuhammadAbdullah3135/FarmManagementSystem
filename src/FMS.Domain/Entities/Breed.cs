using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Breed : BaseEntity
{
    public Guid AnimalTypeId { get; set; }
    public string Name { get; set; } = string.Empty;

    public AnimalType AnimalType { get; set; } = null!;
    public ICollection<Animal> Animals { get; set; } = new List<Animal>();
}
