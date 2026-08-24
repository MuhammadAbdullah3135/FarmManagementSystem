using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class AnimalType : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;

    public Farm Farm { get; set; } = null!;
    public ICollection<Breed> Breeds { get; set; } = new List<Breed>();
    public ICollection<Animal> Animals { get; set; } = new List<Animal>();
}
