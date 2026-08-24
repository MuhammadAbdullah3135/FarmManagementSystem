using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Location : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid LocationTypeId { get; set; }
    public Guid? ParentLocationId { get; set; }

    public Farm Farm { get; set; } = null!;
    public LocationType LocationType { get; set; } = null!;
    public Location? ParentLocation { get; set; }
    public ICollection<Location> ChildLocations { get; set; } = new List<Location>();
    public ICollection<Animal> Animals { get; set; } = new List<Animal>();
}
