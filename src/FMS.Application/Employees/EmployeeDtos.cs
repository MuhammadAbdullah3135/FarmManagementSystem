using FMS.Domain.Enums;

namespace FMS.Application.Employees;

public class DepartmentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CreateDepartmentRequest
{
    public string Name { get; set; } = string.Empty;
}

public class EmployeeRoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateEmployeeRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class EmployeeDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? EmployeeRoleId { get; set; }
    public string? EmployeeRoleName { get; set; }
    public SalaryType SalaryType { get; set; }
    public string SalaryTypeName { get; set; } = string.Empty;
    public decimal SalaryRate { get; set; }
    public DateTime? HireDate { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}

public class CreateEmployeeRequest
{
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
    public string? Notes { get; set; }
}

public class UpdateEmployeeRequest
{
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
}

public class EmployeeListFilter
{
    public string? Search { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? EmployeeRoleId { get; set; }
    public bool? IsActive { get; set; }

    /// <summary>
    /// Delta read: return only rows changed after this instant, plus the ids of employees
    /// deleted since (a soft delete keeps its row, but the delta reports it as a tombstone and
    /// leaves it out of <c>Items</c>). Omitted means the whole list, exactly as before.
    /// </summary>
    public DateTime? UpdatedSince { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SalaryPaymentDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public SalaryType SalaryType { get; set; }
    public string SalaryTypeName { get; set; } = string.Empty;
    public SalaryPaymentType PaymentType { get; set; }
    public string PaymentTypeName { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime? PeriodCovered { get; set; }
    public string? Notes { get; set; }

    /// <summary>Who recorded the payment — a name when the account has one, otherwise the email.</summary>
    public string? RecordedByName { get; set; }
    public string? RecordedByEmail { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class RecordSalaryPaymentRequest
{
    public decimal Amount { get; set; }
    public DateTime? PaymentDate { get; set; }
    public SalaryPaymentType PaymentType { get; set; }
    public string? Reference { get; set; }
    public DateTime? PeriodCovered { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Body for deleting a salary payment. The reason is required by the service and lands in the audit log.</summary>
public class DeleteSalaryPaymentRequest
{
    public string? Reason { get; set; }
}

public class PayrollReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal TotalPaid { get; set; }
    public int PaymentCount { get; set; }

    /// <summary>Current monthly run-rate across active employees (salary types normalized to a month).</summary>
    public decimal ExpectedMonthlyPayroll { get; set; }
    public List<PayrollByEmployeeDto> ByEmployee { get; set; } = new();
}

public class PayrollByEmployeeDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public decimal TotalPaid { get; set; }
    public int PaymentCount { get; set; }
    public DateTime? LastPaymentDate { get; set; }
}
