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

    /// <summary>How the money moved (cash, bank transfer, …). Legacy rows are Unspecified.</summary>
    public SalaryPaymentType PaymentType { get; set; }

    /// <summary>Receipt or transaction number that identifies this payment outside the system.</summary>
    public string? Reference { get; set; }

    /// <summary>The payroll period this payment settles (the month/week the salary is for) — distinct from <see cref="PaymentDate"/>, the day the money moved.</summary>
    public DateTime? PeriodCovered { get; set; }

    public string? Notes { get; set; }

    public Employee Employee { get; set; } = null!;
}
