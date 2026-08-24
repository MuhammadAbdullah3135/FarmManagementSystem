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
    Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid farmId, Guid id, UpdateEmployeeRequest request);
    Task<Result> DeleteEmployeeAsync(Guid farmId, Guid id);

    // Salary payments & payroll
    Task<Result<SalaryPaymentDto>> RecordSalaryPaymentAsync(Guid farmId, Guid employeeId, RecordSalaryPaymentRequest request);
    Task<Result<PagedResult<SalaryPaymentDto>>> GetSalaryPaymentsAsync(Guid farmId, Guid employeeId, int page, int pageSize);
    Task<Result> DeleteSalaryPaymentAsync(Guid farmId, Guid employeeId, Guid paymentId);
    Task<Result<PayrollReportDto>> GetPayrollReportAsync(Guid farmId, DateTime? from, DateTime? to);
}
