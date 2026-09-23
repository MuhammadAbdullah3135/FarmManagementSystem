using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Common;
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

    public async Task<Result<DeltaResult<EmployeeDto>>> GetEmployeesAsync(Guid farmId, EmployeeListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;
        var cursor = DateTime.UtcNow;

        var query = _context.Employees
            .Include(e => e.Department)
            .Include(e => e.EmployeeRole)
            .Where(e => e.FarmId == farmId && !e.IsDeleted);

        if (filter.UpdatedSince.HasValue)
            return await DeltaAsync(farmId, filter, cursor);

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

        var full = new PagedResult<EmployeeDto>
        {
            Items = employees.Select(MapEmployee).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };

        return Result<DeltaResult<EmployeeDto>>.Success(DeltaResult<EmployeeDto>.From(full, cursor));
    }

    /// <summary>
    /// The delta read.
    ///
    /// <para>
    /// Two things make this more than "the same query with a date filter". An employee is
    /// soft-deleted, so a removed row is still <em>there</em> — it is found by the cursor, and
    /// then split out: its id becomes a tombstone and its row is left out of the items, which
    /// is the shape a client can act on uniformly (upsert the items, drop the ids). And the
    /// cursor is <c>COALESCE(ModifiedAt, CreatedAt)</c>: an employee created and never edited
    /// has no ModifiedAt, and matching on that alone would make it invisible forever.
    /// </para>
    /// </summary>
    private async Task<Result<DeltaResult<EmployeeDto>>> DeltaAsync(
        Guid farmId, EmployeeListFilter filter, DateTime cursor)
    {
        if (!DeltaCursor.IsAnswerable(filter.UpdatedSince, cursor))
            return Result<DeltaResult<EmployeeDto>>.Success(DeltaResult<EmployeeDto>.FullSyncRequired(cursor));

        var since = filter.UpdatedSince!.Value;

        // Filters and paging are deliberately not applied here, and the deletion filter the
        // full read uses is not either: a delta answers "what changed in the collection", and
        // a soft delete is a modification, so it is exactly what this read has to be able to
        // see. See DeltaCursor.MaxDeltaRows for why a delta is bounded rather than paged.
        var changed = await _context.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.EmployeeRole)
            .Where(e => e.FarmId == farmId && (e.ModifiedAt ?? e.CreatedAt) > since)
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Take(DeltaCursor.MaxDeltaRows + 1)
            .ToListAsync();

        if (changed.Count > DeltaCursor.MaxDeltaRows)
            return Result<DeltaResult<EmployeeDto>>.Success(DeltaResult<EmployeeDto>.FullSyncRequired(cursor));

        var deletedIds = changed.Where(e => e.IsDeleted).Select(e => e.Id).ToList();

        // A hard delete should not exist for an employee, but if one ever happens the audit log
        // still knows the id, and one indexed read is cheaper than a client that keeps showing
        // somebody who is gone.
        deletedIds.AddRange(await ChangeTombstones.HardDeletedIdsAsync<Employee>(_context, farmId, since));

        var live = changed.Where(e => !e.IsDeleted).Select(MapEmployee).ToList();

        var delta = new DeltaResult<EmployeeDto>
        {
            Items = live,
            Page = 1,
            PageSize = live.Count,
            TotalCount = live.Count,
            Cursor = cursor,
            DeletedIds = deletedIds.Distinct().ToList()
        };

        return Result<DeltaResult<EmployeeDto>>.Success(delta);
    }

    public async Task<Result<EmployeeDto>> CreateEmployeeAsync(Guid farmId, CreateEmployeeRequest request)
    {
        // One employee, the endpoint's own path: the rules come from EmployeeRules,
        // shared with the bulk import, and the answer is still the first message.
        var errors = EmployeeRules.ValidateDetails(
            request.FirstName, request.LastName, request.Email, request.Phone,
            request.Address, request.SalaryRate, request.HireDate, request.Notes);
        if (errors.Count > 0)
            return Result<EmployeeDto>.Validation(errors[0].Message);

        var referenceError = await ValidateReferencesAsync(farmId, request.DepartmentId, request.EmployeeRoleId);
        if (referenceError != null)
            return Result<EmployeeDto>.Failure(referenceError);

        // The email is the employee's identifier, so a duplicate is refused here exactly
        // as the import refuses it. Without this the import would be the stricter path,
        // and a duplicate could be created by hand that a file cannot create.
        if (EmployeeRules.EmailKey(request.Email) is { } emailKey &&
            await _context.Employees.AnyAsync(employee =>
                employee.FarmId == farmId
                && !employee.IsDeleted
                && employee.Email != null
                && employee.Email.ToLower() == emailKey))
        {
            return Result<EmployeeDto>.Conflict(EmployeeRules.DuplicateEmailMessage);
        }

        var employee = EmployeeRules.Build(farmId, request, _currentUser.GetUserId(), DateTime.UtcNow);
        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        return await GetEmployeeByIdAsync(farmId, employee.Id);
    }

    /// <summary>
    /// Validates a batch without writing it, using the same rules, the same reference
    /// checks and the same duplicate check as <see cref="CreateEmployeeAsync"/>, so what
    /// this predicts is what <see cref="CreateEmployeesAsync"/> does.
    /// </summary>
    public Task<Result<BulkCreateResultDto>> ValidateEmployeesAsync(
        Guid farmId, IReadOnlyList<CreateEmployeeRequest> requests) =>
        CheckBatchAsync(farmId, requests);

    /// <summary>
    /// Creates every employee in a single SaveChanges, which EF wraps in one
    /// transaction — so a batch is atomic: either every employee is written or none is.
    /// That is what the import's all-or-nothing promise rests on, on PostgreSQL and on
    /// the InMemory test provider alike.
    /// </summary>
    public async Task<Result<BulkCreateResultDto>> CreateEmployeesAsync(
        Guid farmId, IReadOnlyList<CreateEmployeeRequest> requests)
    {
        var checkedBatch = await CheckBatchAsync(farmId, requests);
        if (!checkedBatch.IsSuccess)
            return checkedBatch;

        if (checkedBatch.Value!.Failures.Count > 0)
            return checkedBatch;

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        foreach (var request in requests)
            _context.Employees.Add(EmployeeRules.Build(farmId, request, userId, now));

        await _context.SaveChangesAsync();

        return Result<BulkCreateResultDto>.Success(new BulkCreateResultDto
        {
            RequestedCount = requests.Count,
            SuccessCount = requests.Count,
            Failures = new List<BulkCreateFailureDto>()
        });
    }

    /// <summary>
    /// The batch's own checks: every field rule per row, the department and role the row
    /// names, then the email uniqueness the identifier rule requires.
    ///
    /// Existing addresses and the farm's departments and roles are loaded in one query
    /// each rather than one per row, and the batch's own addresses are compared against
    /// each other too — without that second check a file naming the same person twice
    /// would create two employees, since both rows pass individually.
    /// </summary>
    private async Task<Result<BulkCreateResultDto>> CheckBatchAsync(
        Guid farmId, IReadOnlyList<CreateEmployeeRequest> requests)
    {
        var failures = new List<BulkCreateFailureDto>();

        var emailKeys = requests
            .Select(request => EmployeeRules.EmailKey(request.Email))
            .Where(key => key is not null)
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingEmails = emailKeys.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _context.Employees
                .Where(employee => employee.FarmId == farmId
                    && !employee.IsDeleted
                    && employee.Email != null
                    && emailKeys.Contains(employee.Email.ToLower()))
                .Select(employee => employee.Email!)
                .ToListAsync())
                .Select(EmployeeRules.EmailKey)
                .Where(key => key is not null)
                .Select(key => key!)
                .ToHashSet(StringComparer.Ordinal);

        var departmentIds = (await _context.Departments
            .Where(department => department.FarmId == farmId)
            .Select(department => department.Id)
            .ToListAsync()).ToHashSet();

        var roleIds = (await _context.EmployeeRoles
            .Where(role => role.FarmId == farmId)
            .Select(role => role.Id)
            .ToListAsync()).ToHashSet();

        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var errors = EmployeeRules.ValidateDetails(
                request.FirstName, request.LastName, request.Email, request.Phone,
                request.Address, request.SalaryRate, request.HireDate, request.Notes);

            if (errors.Count > 0)
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = errors[0].Message });
                continue;
            }

            // The same messages the endpoint answers with for these two references.
            if (request.DepartmentId.HasValue && !departmentIds.Contains(request.DepartmentId.Value))
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = "Department not found" });
                continue;
            }

            if (request.EmployeeRoleId.HasValue && !roleIds.Contains(request.EmployeeRoleId.Value))
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = "Role not found" });
                continue;
            }

            if (EmployeeRules.EmailKey(request.Email) is not { } key)
                continue;

            if (existingEmails.Contains(key))
            {
                failures.Add(new BulkCreateFailureDto { Index = index, Message = EmployeeRules.DuplicateEmailMessage });
                continue;
            }

            if (!seenInBatch.Add(key))
            {
                failures.Add(new BulkCreateFailureDto
                    { Index = index, Message = $"The batch contains the same email '{request.Email!.Trim()}' twice" });
            }
        }

        return Result<BulkCreateResultDto>.Success(new BulkCreateResultDto
        {
            RequestedCount = requests.Count,
            SuccessCount = requests.Count - failures.Count,
            Failures = failures
        });
    }

    public async Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid farmId, Guid id, UpdateEmployeeRequest request)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == id && e.FarmId == farmId && !e.IsDeleted);
        if (employee == null)
            return Result<EmployeeDto>.NotFound("Employee not found");

        var validation = EmployeeRules.ValidateDetails(
            request.FirstName, request.LastName, request.Email, request.Phone,
            request.Address, request.SalaryRate, request.HireDate, request.Notes);

        if (validation.Count > 0)
            return Result<EmployeeDto>.Validation(validation[0].Message);

        var referenceError = await ValidateReferencesAsync(farmId, request.DepartmentId, request.EmployeeRoleId);
        if (referenceError != null)
            return Result<EmployeeDto>.Failure(referenceError);

        // The identifier rule applies to an edit as well: without this, the uniqueness the
        // create path and the import both enforce could be undone by renaming a second
        // employee onto the first one's address.
        if (EmployeeRules.EmailKey(request.Email) is { } emailKey &&
            await _context.Employees.AnyAsync(other =>
                other.FarmId == farmId
                && other.Id != id
                && !other.IsDeleted
                && other.Email != null
                && other.Email.ToLower() == emailKey))
        {
            return Result<EmployeeDto>.Conflict(EmployeeRules.DuplicateEmailMessage);
        }

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

    /// <summary>
    /// Retained for the department and role create paths below. The employee's own field
    /// rules moved to <see cref="EmployeeRules"/>, which the bulk import shares.
    /// </summary>
    private static string? ValidateName(string name, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"{fieldLabel} is required";
        if (name.Length > 100)
            return $"{fieldLabel} cannot exceed 100 characters";
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
