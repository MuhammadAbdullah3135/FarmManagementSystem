using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Account : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Farm> Farms { get; set; } = new List<Farm>();
}
