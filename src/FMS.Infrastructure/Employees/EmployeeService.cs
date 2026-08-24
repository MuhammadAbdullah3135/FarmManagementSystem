using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Employees;

public class EmployeeService : IEmployeeService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public EmployeeService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // Departments

    public async Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(Guid farmId)
    {
        var departments = await _context.Departments
            .Where(d => d.FarmId == farmId)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentDto { Id = d.Id, Name = d.Name })
            .ToListAsync();

        return Result<List<DepartmentDto>>.Success(departments);
    }

    public async Task<Result<DepartmentDto>> CreateDepartmentAsync(Guid farmId, CreateDepartmentRequest request)
    {
        var validationError = ValidateName(request.Name, "Department name");
        if (validationError != null)
            return Result<DepartmentDto>.Validation(validationError);

        var duplicate = await _context.Departments
            .AnyAsync(d => d.FarmId == farmId && d.Name.ToLower() == request.Name.ToLower());
        if (duplicate)
            return Result<DepartmentDto>.Conflict($"A department named '{request.Name}' already exists");

        var department = new Department
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.Departments.Add(department);
        await _context.SaveChangesAsync();

        return Result<DepartmentDto>.Success(new DepartmentDto { Id = department.Id, Name = department.Name });
    }

    public async Task<Result> DeleteDepartmentAsync(Guid farmId, Guid id)
    {
        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == id && d.FarmId == farmId);
        if (department == null)
            return Result.NotFound("Department not found");

        var hasEmployees = await _context.Employees
            .AnyAsync(e => e.DepartmentId == id);
        if (hasEmployees)
            return Result.Conflict("Cannot delete a department that has employees");

        _context.Departments.Remove(department);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Employee roles

    public async Task<Result<List<EmployeeRoleDto>>> GetRolesAsync(Guid farmId)
    {
        var roles = await _context.EmployeeRoles
            .Where(r => r.FarmId == farmId)
            .OrderBy(r => r.Name)
            .Select(r => new EmployeeRoleDto { Id = r.Id, Name = r.Name, Description = r.Description })
            .ToListAsync();

        return Result<List<EmployeeRoleDto>>.Success(roles);
    }

    public async Task<Result<EmployeeRoleDto>> CreateRoleAsync(Guid farmId, CreateEmployeeRoleRequest request)
    {
        var validationError = ValidateName(request.Name, "Role name");
        if (validationError != null)
            return Result<EmployeeRoleDto>.Validation(validationError);

        if (request.Description?.Length > 500)
            return Result<EmployeeRoleDto>.Validation("Description cannot exceed 500 characters");

        var duplicate = await _context.EmployeeRoles
            .AnyAsync(r => r.FarmId == farmId && r.Name.ToLower() == request.Name.ToLower());
        if (duplicate)
            return Result<EmployeeRoleDto>.Conflict($"A role named '{request.Name}' already exists");

        var role = new EmployeeRole
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        _context.EmployeeRoles.Add(role);
        await _context.SaveChangesAsync();

        return Result<EmployeeRoleDto>.Success(new EmployeeRoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description
        });
    }

    public async Task<Result> DeleteRoleAsync(Guid farmId, Guid id)
    {
        var role = await _context.EmployeeRoles
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (role == null)
            return Result.NotFound("Role not found");

        var hasEmployees = await _context.Employees
            .AnyAsync(e => e.EmployeeRoleId == id);
        if (hasEmployees)
            return Result.Conflict("Cannot delete a role that has employees");

        _context.EmployeeRoles.Remove(role);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Employees

    public async Task<Result<EmployeeDto>> GetEmployeeByIdAsync(Guid farmId, Guid id)
    {
        var employee = await _context.Employees
            .Include(e => e.Department)
            .Include(e => e.EmployeeRole)
            .FirstOrDefaultAsync(e => e.Id == id && e.FarmId == farmId && !e.IsDeleted);
        if (employee == null)
            return Result<EmployeeDto>.NotFound("Employee not found");

        return Result<EmployeeDto>.Success(MapEmployee(employee));
    }

    public async Task<Result<PagedResult<EmployeeDto>>> GetEmployeesAsync(Guid farmId, EmployeeListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = _context.Employees
            .Include(e => e.Department)
            .Include(e => e.EmployeeRole)
            .Where(e => e.FarmId == farmId && !e.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(term) ||
                e.LastName.ToLower().Contains(term) ||
                (e.Phone != null && e.Phone.Contains(term)) ||
                (e.Email != null && e.Email.ToLower().Contains(term)));
        }
        if (filter.DepartmentId.HasValue)
            query = query.Where(e => e.DepartmentId == filter.DepartmentId.Value);
        if (filter.EmployeeRoleId.HasValue)
            query = query.Where(e => e.EmployeeRoleId == filter.EmployeeRoleId.Value);
        if (filter.IsActive.HasValue)
            query = query.Where(e => e.IsActive == filter.IsActive.Value);

        var totalCount = await query.CountAsync();

        var employees = await query
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<EmployeeDto>>.Success(new PagedResult<EmployeeDto>
        {
            Items = employees.Select(MapEmployee).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<EmployeeDto>> CreateEmployeeAsync(Guid farmId, CreateEmployeeRequest request)
    {
        var validationError = ValidateEmployeeDetails(request.FirstName, request.LastName, request.Email, request.SalaryRate, request.HireDate);
        if (validationError != null)
            return Result<EmployeeDto>.Validation(validationError);

        var referenceError = await ValidateReferencesAsync(farmId, request.DepartmentId, request.EmployeeRoleId);
        if (referenceError != null)
            return Result<EmployeeDto>.Failure(referenceError);

        var now = DateTime.UtcNow;
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            DepartmentId = request.DepartmentId,
            EmployeeRoleId = request.EmployeeRoleId,
            SalaryType = request.SalaryType,
            SalaryRate = Math.Round(request.SalaryRate, 2),
            HireDate = request.HireDate,
            IsActive = true,
            Notes = request.Notes,
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        return await GetEmployeeByIdAsync(farmId, employee.Id);
    }

    public async Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid farmId, Guid id, UpdateEmployeeRequest request)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.FarmId == farmId && !e.IsDeleted);
        if (employee == null)
            return Result<EmployeeDto>.NotFound("Employee not found");

        var validationError = ValidateEmployeeDetails(request.FirstName, request.LastName, request.Email, request.SalaryRate, request.HireDate);
        if (validationError != null)
            return Result<EmployeeDto>.Validation(validationError);

        var referenceError = await ValidateReferencesAsync(farmId, request.DepartmentId, request.EmployeeRoleId);
        if (referenceError != null)
            return Result<EmployeeDto>.Failure(referenceError);

        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.Phone = request.Phone?.Trim();
        employee.Email = request.Email?.Trim();
        employee.Address = request.Address?.Trim();
        employee.DepartmentId = request.DepartmentId;
        employee.EmployeeRoleId = request.EmployeeRoleId;
        employee.SalaryType = request.SalaryType;
        employee.SalaryRate = Math.Round(request.SalaryRate, 2);
        employee.HireDate = request.HireDate;
        employee.IsActive = request.IsActive;
        employee.Notes = request.Notes;
        employee.ModifiedAt = DateTime.UtcNow;
        employee.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await GetEmployeeByIdAsync(farmId, id);
    }

    public async Task<Result> DeleteEmployeeAsync(Guid farmId, Guid id)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.FarmId == farmId && !e.IsDeleted);
        if (employee == null)
            return Result.NotFound("Employee not found");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        employee.IsDeleted = true;
        employee.DeletedAt = now;
        employee.DeletedBy = userId;
        employee.IsActive = false;
        employee.ModifiedAt = now;
        employee.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Salary payments & payroll

    public async Task<Result<SalaryPaymentDto>> RecordSalaryPaymentAsync(Guid farmId, Guid employeeId, RecordSalaryPaymentRequest request)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.FarmId == farmId);
        if (employee == null)
            return Result<SalaryPaymentDto>.NotFound("Employee not found");

        if (request.Amount <= 0)
            return Result<SalaryPaymentDto>.Validation("Amount must be greater than zero");

        var paymentDate = request.PaymentDate ?? DateTime.UtcNow;
        if (paymentDate > DateTime.UtcNow.AddMinutes(5))
            return Result<SalaryPaymentDto>.Validation("Payment date cannot be in the future");

        var payment = new SalaryPayment
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            EmployeeId = employee.Id,
            Amount = Math.Round(request.Amount, 2),
            PaymentDate = paymentDate,
            SalaryType = employee.SalaryType,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.SalaryPayments.Add(payment);
        await _context.SaveChangesAsync();

        return Result<SalaryPaymentDto>.Success(new SalaryPaymentDto
        {
            Id = payment.Id,
            EmployeeId = payment.EmployeeId,
            EmployeeName = $"{employee.FirstName} {employee.LastName}",
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            SalaryType = payment.SalaryType,
            SalaryTypeName = payment.SalaryType.ToString(),
            Notes = payment.Notes
        });
    }

    public async Task<Result<PagedResult<SalaryPaymentDto>>> GetSalaryPaymentsAsync(Guid farmId, Guid employeeId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var employeeExists = await _context.Employees
            .AnyAsync(e => e.Id == employeeId && e.FarmId == farmId);
        if (!employeeExists)
            return Result<PagedResult<SalaryPaymentDto>>.NotFound("Employee not found");

        var query = _context.SalaryPayments
            .Include(p => p.Employee)
            .Where(p => p.FarmId == farmId && p.EmployeeId == employeeId);

        var totalCount = await query.CountAsync();

        var payments = await query
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<SalaryPaymentDto>>.Success(new PagedResult<SalaryPaymentDto>
        {
            Items = payments.Select(MapPayment).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result> DeleteSalaryPaymentAsync(Guid farmId, Guid employeeId, Guid paymentId)
    {
        var payment = await _context.SalaryPayments
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.EmployeeId == employeeId && p.FarmId == farmId);
        if (payment == null)
            return Result.NotFound("Salary payment not found");

        _context.SalaryPayments.Remove(payment);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<PayrollReportDto>> GetPayrollReportAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var effectiveTo = (to ?? DateTime.UtcNow).Date;
        var effectiveFrom = (from ?? effectiveTo.AddDays(-30)).Date;
        if (effectiveFrom > effectiveTo)
            return Result<PayrollReportDto>.Validation("From date must be before or equal to To date");

        var payments = await _context.SalaryPayments
            .Include(p => p.Employee)
            .Where(p => p.FarmId == farmId &&
                        p.PaymentDate >= effectiveFrom &&
                        p.PaymentDate < effectiveTo.AddDays(1))
            .ToListAsync();

        var activeEmployees = await _context.Employees
            .Where(e => e.FarmId == farmId && !e.IsDeleted && e.IsActive)
            .ToListAsync();

        var report = new PayrollReportDto
        {
            From = effectiveFrom,
            To = effectiveTo,
            TotalPaid = payments.Sum(p => p.Amount),
            PaymentCount = payments.Count,
            ExpectedMonthlyPayroll = Math.Round(activeEmployees.Sum(ToMonthlyRate), 2),
            ByEmployee = payments
                .GroupBy(p => new { p.EmployeeId, p.Employee.FirstName, p.Employee.LastName })
                .Select(g => new PayrollByEmployeeDto
                {
                    EmployeeId = g.Key.EmployeeId,
                    EmployeeName = $"{g.Key.FirstName} {g.Key.LastName}",
                    TotalPaid = g.Sum(p => p.Amount),
                    PaymentCount = g.Count(),
                    LastPaymentDate = g.Max(p => p.PaymentDate)
                })
                .OrderByDescending(x => x.TotalPaid)
                .ToList()
        };

        return Result<PayrollReportDto>.Success(report);
    }

    // Helpers

    /// <summary>Normalizes any salary type to a monthly equivalent run-rate.</summary>
    private static decimal ToMonthlyRate(Employee employee) => employee.SalaryType switch
    {
        SalaryType.Monthly => employee.SalaryRate,
        SalaryType.Weekly => employee.SalaryRate * 52m / 12m,
        SalaryType.Daily => employee.SalaryRate * 30m,
        SalaryType.Hourly => employee.SalaryRate * 176m, // 8h × 22 working days
        _ => 0m
    };

    private static string? ValidateName(string name, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"{fieldLabel} is required";
        if (name.Length > 100)
            return $"{fieldLabel} cannot exceed 100 characters";
        return null;
    }

    private static string? ValidateEmployeeDetails(string firstName, string lastName, string? email, decimal salaryRate, DateTime? hireDate)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            return "First name is required";
        if (firstName.Length > 100)
            return "First name cannot exceed 100 characters";
        if (string.IsNullOrWhiteSpace(lastName))
            return "Last name is required";
        if (lastName.Length > 100)
            return "Last name cannot exceed 100 characters";
        if (!string.IsNullOrWhiteSpace(email))
        {
            var trimmed = email.Trim();
            if (!trimmed.Contains('@') || trimmed.StartsWith("@") || trimmed.EndsWith("@"))
                return "Email address is not valid";
        }
        if (salaryRate < 0)
            return "Salary rate cannot be negative";
        if (hireDate.HasValue && hireDate.Value.Date > DateTime.UtcNow.Date)
            return "Hire date cannot be in the future";
        return null;
    }

    private async Task<Error?> ValidateReferencesAsync(Guid farmId, Guid? departmentId, Guid? employeeRoleId)
    {
        if (departmentId.HasValue &&
            !await _context.Departments.AnyAsync(d => d.Id == departmentId.Value && d.FarmId == farmId))
            return Error.NotFound("Department not found");

        if (employeeRoleId.HasValue &&
            !await _context.EmployeeRoles.AnyAsync(r => r.Id == employeeRoleId.Value && r.FarmId == farmId))
            return Error.NotFound("Role not found");

        return null;
    }

    private static EmployeeDto MapEmployee(Employee employee) => new()
    {
        Id = employee.Id,
        FirstName = employee.FirstName,
        LastName = employee.LastName,
        Phone = employee.Phone,
        Email = employee.Email,
        Address = employee.Address,
        DepartmentId = employee.DepartmentId,
        DepartmentName = employee.Department?.Name,
        EmployeeRoleId = employee.EmployeeRoleId,
        EmployeeRoleName = employee.EmployeeRole?.Name,
        SalaryType = employee.SalaryType,
        SalaryTypeName = employee.SalaryType.ToString(),
        SalaryRate = employee.SalaryRate,
        HireDate = employee.HireDate,
        IsActive = employee.IsActive,
        Notes = employee.Notes
    };

    private static SalaryPaymentDto MapPayment(SalaryPayment payment) => new()
    {
        Id = payment.Id,
        EmployeeId = payment.EmployeeId,
        EmployeeName = $"{payment.Employee.FirstName} {payment.Employee.LastName}",
        Amount = payment.Amount,
        PaymentDate = payment.PaymentDate,
        SalaryType = payment.SalaryType,
        SalaryTypeName = payment.SalaryType.ToString(),
        Notes = payment.Notes
    };
}
