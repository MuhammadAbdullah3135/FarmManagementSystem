using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class Department : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;

    public Farm Farm { get; set; } = null!;
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
