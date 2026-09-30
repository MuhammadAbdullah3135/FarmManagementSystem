using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

public class SalaryPayment : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }

    /// <summary>Snapshot of the employee's salary type at payment time.</summary>
    public SalaryType SalaryType { get; set; }
    public string? Notes { get; set; }

    public Employee Employee { get; set; } = null!;
}
