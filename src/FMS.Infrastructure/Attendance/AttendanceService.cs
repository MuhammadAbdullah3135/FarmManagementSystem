using FMS.Application.Attendance;
using FMS.Application.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Attendance;

public class AttendanceService : IAttendanceService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public AttendanceService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<AttendanceRecordDto>> CheckInAsync(Guid farmId, Guid employeeId)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.FarmId == farmId);
        if (employee == null)
            return Result<AttendanceRecordDto>.NotFound("Employee not found");

        var today = DateTime.UtcNow.Date;
        var alreadyCheckedIn = await _context.AttendanceRecords
            .AnyAsync(r => r.EmployeeId == employeeId && r.Date == today);
        if (alreadyCheckedIn)
            return Result<AttendanceRecordDto>.Conflict("Employee already has an attendance record for today");

        var now = DateTime.UtcNow;
        var record = new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            EmployeeId = employeeId,
            Date = today,
            Status = AttendanceStatus.Present,
            CheckInAt = now,
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.AttendanceRecords.Add(record);
        await _context.SaveChangesAsync();

        return Result<AttendanceRecordDto>.Success(MapRecord(record, employee));
    }

    public async Task<Result<AttendanceRecordDto>> CheckOutAsync(Guid farmId, Guid employeeId)
    {
        var record = await _context.AttendanceRecords
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.FarmId == farmId &&
                                      r.Date == DateTime.UtcNow.Date);
        if (record == null)
            return Result<AttendanceRecordDto>.NotFound("No attendance record found for today");

        if (record.CheckOutAt.HasValue)
            return Result<AttendanceRecordDto>.Conflict("Employee has already checked out today");

        record.CheckOutAt = DateTime.UtcNow;
        record.ModifiedAt = DateTime.UtcNow;
        record.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<AttendanceRecordDto>.Success(MapRecord(record, record.Employee));
    }

    public async Task<Result<AttendanceRecordDto>> UpsertAttendanceAsync(Guid farmId, UpsertAttendanceRequest request)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.FarmId == farmId);
        if (employee == null)
            return Result<AttendanceRecordDto>.NotFound("Employee not found");

        var date = request.Date.Date;
        if (date > DateTime.UtcNow.Date)
            return Result<AttendanceRecordDto>.Validation("Attendance cannot be recorded for a future date");

        if (request.CheckInAt.HasValue && request.CheckOutAt.HasValue && request.CheckOutAt.Value < request.CheckInAt.Value)
            return Result<AttendanceRecordDto>.Validation("Check-out time cannot be before check-in time");

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Length > 1000)
            return Result<AttendanceRecordDto>.Validation("Notes cannot exceed 1000 characters");

        var now = DateTime.UtcNow;
        var record = await _context.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeId == request.EmployeeId && r.FarmId == farmId && r.Date == date);

        if (record == null)
        {
            record = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                EmployeeId = request.EmployeeId,
                Date = date,
                CreatedAt = now,
                CreatedBy = _currentUser.GetUserId()
            };
            _context.AttendanceRecords.Add(record);
        }
        else
        {
            record.ModifiedAt = now;
            record.ModifiedBy = _currentUser.GetUserId();
        }

        record.Status = request.Status;
        record.CheckInAt = request.CheckInAt;
        record.CheckOutAt = request.CheckOutAt;
        record.Notes = request.Notes?.Trim();

        await _context.SaveChangesAsync();

        return Result<AttendanceRecordDto>.Success(MapRecord(record, employee));
    }

    public async Task<Result<PagedResult<AttendanceRecordDto>>> GetAttendanceAsync(Guid farmId, AttendanceListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = _context.AttendanceRecords
            .AsNoTracking()
            .Include(r => r.Employee)
            .Where(r => r.FarmId == farmId);

        if (filter.EmployeeId.HasValue)
            query = query.Where(r => r.EmployeeId == filter.EmployeeId.Value);

        if (filter.Status.HasValue)
            query = query.Where(r => r.Status == filter.Status.Value);

        if (filter.From.HasValue)
            query = query.Where(r => r.Date >= filter.From.Value.Date);

        if (filter.To.HasValue)
            query = query.Where(r => r.Date <= filter.To.Value.Date);

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderByDescending(r => r.Date)
            .ThenBy(r => r.Employee.LastName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<AttendanceRecordDto>>.Success(new PagedResult<AttendanceRecordDto>
        {
            Items = records.Select(r => MapRecord(r, r.Employee)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result> DeleteAttendanceAsync(Guid farmId, Guid id)
    {
        var record = await _context.AttendanceRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (record == null)
            return Result.NotFound("Attendance record not found");

        _context.AttendanceRecords.Remove(record);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Helpers

    private static AttendanceRecordDto MapRecord(AttendanceRecord record, Employee employee) => new()
    {
        Id = record.Id,
        EmployeeId = record.EmployeeId,
        EmployeeName = $"{employee.FirstName} {employee.LastName}",
        Date = record.Date,
        Status = record.Status,
        StatusName = record.Status.ToString(),
        CheckInAt = record.CheckInAt,
        CheckOutAt = record.CheckOutAt,
        HoursWorked = record.CheckInAt.HasValue && record.CheckOutAt.HasValue
            ? Math.Round((decimal)(record.CheckOutAt.Value - record.CheckInAt.Value).TotalHours, 2)
            : null,
        Notes = record.Notes
    };
}
