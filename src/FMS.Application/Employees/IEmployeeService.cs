using FMS.Application.Common;

namespace FMS.Application.Employees;

public interface IEmployeeService
{
    // Departments
    Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(Guid farmId);
    Task<Result<DepartmentDto>> CreateDepartmentAsync(Guid farmId, CreateDepartmentRequest request);
    Task<Result> DeleteDepartmentAsync(Guid farmId, Guid id);

    // Employee roles
    Task<Result<List<EmployeeRoleDto>>> GetRolesAsync(Guid farmId);
    Task<Result<EmployeeRoleDto>> CreateRoleAsync(Guid farmId, CreateEmployeeRoleRequest request);
    Task<Result> DeleteRoleAsync(Guid farmId, Guid id);

    // Employees
    Task<Result<EmployeeDto>> GetEmployeeByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<EmployeeDto>>> GetEmployeesAsync(Guid farmId, EmployeeListFilter filter);
    Task<Result<EmployeeDto>> CreateEmployeeAsync(Guid farmId, CreateEmployeeRequest request);

    /// <summary>
    /// Validates a batch without writing it. Same rules, same reference checks and same
    /// identifier check as <see cref="CreateEmployeeAsync"/>, reported per row, so a bulk
    /// caller can preview a file and get exactly the answer a commit would give.
    /// </summary>
    Task<Result<BulkCreateResultDto>> ValidateEmployeesAsync(Guid farmId, IReadOnlyList<CreateEmployeeRequest> requests);

    /// <summary>Creates the whole batch in one SaveChanges, so it is atomic.</summary>
    Task<Result<BulkCreateResultDto>> CreateEmployeesAsync(Guid farmId, IReadOnlyList<CreateEmployeeRequest> requests);
    Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid farmId, Guid id, UpdateEmployeeRequest request);
    Task<Result> DeleteEmployeeAsync(Guid farmId, Guid id);

    // Salary payments & payroll
    Task<Result<SalaryPaymentDto>> RecordSalaryPaymentAsync(Guid farmId, Guid employeeId, RecordSalaryPaymentRequest request);
    Task<Result<PagedResult<SalaryPaymentDto>>> GetSalaryPaymentsAsync(Guid farmId, Guid employeeId, int page, int pageSize);
    Task<Result> DeleteSalaryPaymentAsync(Guid farmId, Guid employeeId, Guid paymentId);
    Task<Result<PayrollReportDto>> GetPayrollReportAsync(Guid farmId, DateTime? from, DateTime? to);
}
