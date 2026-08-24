using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class Employee : SoftDeleteEntity
{
    public Guid FarmId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? EmployeeRoleId { get; set; }
    public SalaryType SalaryType { get; set; }
    public decimal SalaryRate { get; set; }
    public DateTime? HireDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public Farm Farm { get; set; } = null!;
    public Department? Department { get; set; }
    public EmployeeRole? EmployeeRole { get; set; }
}
