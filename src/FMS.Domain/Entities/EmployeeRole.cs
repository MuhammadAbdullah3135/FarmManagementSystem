using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class EmployeeRole : BaseEntity
{
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Farm Farm { get; set; } = null!;
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
