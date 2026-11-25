using FMS.Application.Attendance;
using FMS.Application.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Common;
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

    public async Task<Result<AttendanceRecordDto>> CheckInAsync(
        Guid farmId, Guid employeeId, CheckInRequest? request = null, Guid? mutationId = null)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.FarmId == farmId);
        if (employee == null)
            return Result<AttendanceRecordDto>.NotFound("Employee not found", DomainMessageKeys.EmployeeNotFound);

        // A queued check-in carries the time the device was at the gate, so the record belongs
        // to that day rather than to the day the sync happened to reach the server. A live
        // check-in supplies nothing and lands on today, exactly as before.
        var now = DateTime.UtcNow;
        var occurredAt = request?.OccurredAt ?? now;
        if (MutationTimestampRules.IsTooFarInTheFuture(occurredAt, now))
            return Result<AttendanceRecordDto>.Validation(MutationTimestampRules.CheckInOccurredAtMessage);

        var date = occurredAt.Date;
        var deviceTime = request?.OccurredAt;

        // Multiple sessions per day are allowed (breaks/split shifts). Only an open session
        // (checked in, not checked out) blocks a new check-in.
        var openSession = await _context.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.Date == date && r.CheckInAt != null && r.CheckOutAt == null)
            .OrderByDescending(r => r.CheckInAt)
            .FirstOrDefaultAsync();

        if (openSession is null)
        {
            // A manual day-level record with no check-in time (Leave/Absent, etc.) is deliberate;
            // do not overwrite it with a device check-in.
            var manualDay = await _context.AttendanceRecords
                .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.Date == date && r.CheckInAt == null);
            if (manualDay is not null)
                return Result<AttendanceRecordDto>.Superseded(
                    $"That day already has an attendance record marked {manualDay.Status}; it was not changed",
                    DomainMessageKeys.AttendanceAlreadyMarked,
                    new Dictionary<string, object?> { ["status"] = manualDay.Status.ToString() });

            var record = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                EmployeeId = employeeId,
                Date = date,
                Status = AttendanceStatus.Present,
                CheckInAt = occurredAt,
                ClientMutationId = mutationId,
                CreatedAt = now,
                CreatedBy = _currentUser.GetUserId()
            };

            _context.AttendanceRecords.Add(record);
            await _context.SaveChangesAsync();

            return Result<AttendanceRecordDto>.Success(MapRecord(record, employee));
        }

        // Only a device's own report of when the employee was at the gate may move the stored
        // time. A live check-in supplies none — the server's clock says when the request
        // arrived, not when the employee did — so an open session simply stands.
        if (deviceTime is null)
            return Result<AttendanceRecordDto>.Conflict(
                "Employee is already checked in", DomainMessageKeys.AttendanceAlreadyRecorded);

        // Earliest check-in wins for an open session. A later/equal queued time is already
        // satisfied; an earlier queued time moves the session start back.
        if (deviceTime.Value >= openSession.CheckInAt!.Value)
            return Result<AttendanceRecordDto>.Superseded(
                "Employee is already checked in", DomainMessageKeys.AttendanceAlreadyRecorded);

        openSession.CheckInAt = occurredAt;
        openSession.ClientMutationId ??= mutationId;
        openSession.ModifiedAt = now;
        openSession.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<AttendanceRecordDto>.Success(MapRecord(openSession, employee));
    }

    public async Task<Result<AttendanceRecordDto>> CheckOutAsync(
        Guid farmId, Guid employeeId, CheckOutRequest? request = null, Guid? mutationId = null)
    {
        var now = DateTime.UtcNow;
        var deviceTime = request?.OccurredAt;
        var occurredAt = deviceTime ?? now;
        if (MutationTimestampRules.IsTooFarInTheFuture(occurredAt, now))
            return Result<AttendanceRecordDto>.Validation(MutationTimestampRules.CheckOutOccurredAtMessage);

        var date = occurredAt.Date;
        var openSession = await _context.AttendanceRecords
            .Include(r => r.Employee)
            .Where(r => r.EmployeeId == employeeId && r.FarmId == farmId && r.Date == date && r.CheckInAt != null && r.CheckOutAt == null)
            .OrderByDescending(r => r.CheckInAt)
            .FirstOrDefaultAsync();

        if (openSession is null)
        {
            var lastClosed = await _context.AttendanceRecords
                .Include(r => r.Employee)
                .Where(r => r.EmployeeId == employeeId && r.FarmId == farmId && r.Date == date && r.CheckOutAt != null)
                .OrderByDescending(r => r.CheckOutAt)
                .FirstOrDefaultAsync();

            if (lastClosed is null)
                return Result<AttendanceRecordDto>.NotFound("No attendance record found for today", DomainMessageKeys.NoAttendanceToday);

            // No open session: all today's sessions are closed. A live check-out is a conflict.
            // A queued later check-out moves the last closed session forward (latest wins), which
            // keeps offline reconciliation practical; earlier/equal is superseded.
            if (deviceTime is null)
                return Result<AttendanceRecordDto>.Conflict("Employee has already checked out today", DomainMessageKeys.AlreadyCheckedOut);

            if (deviceTime.Value <= lastClosed.CheckOutAt!.Value)
                return Result<AttendanceRecordDto>.Superseded(
                    "Employee has already checked out today", DomainMessageKeys.AlreadyCheckedOut);

            if (occurredAt < lastClosed.CheckInAt)
                return Result<AttendanceRecordDto>.Validation("Check-out time cannot be before check-in time");

            lastClosed.CheckOutAt = occurredAt;
            lastClosed.ClientMutationId ??= mutationId;
            lastClosed.ModifiedAt = now;
            lastClosed.ModifiedBy = _currentUser.GetUserId();
            await _context.SaveChangesAsync();
            return Result<AttendanceRecordDto>.Success(MapRecord(lastClosed, lastClosed.Employee));
        }

        if (occurredAt < openSession.CheckInAt)
            return Result<AttendanceRecordDto>.Validation("Check-out time cannot be before check-in time");

        openSession.CheckOutAt = occurredAt;

        // The row keeps the id of the first queued mutation that wrote it: a check-out does not
        // displace the check-in that created the session. Both mutations are recorded in the ledger.
        openSession.ClientMutationId ??= mutationId;

        openSession.ModifiedAt = now;
        openSession.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<AttendanceRecordDto>.Success(MapRecord(openSession, openSession.Employee));
    }

    public async Task<Result<AttendanceRecordDto>> UpsertAttendanceAsync(Guid farmId, UpsertAttendanceRequest request)
    {
        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.FarmId == farmId);
        if (employee == null)
            return Result<AttendanceRecordDto>.NotFound("Employee not found", DomainMessageKeys.EmployeeNotFound);

        var date = request.Date.Date;
        if (date > DateTime.UtcNow.Date)
            return Result<AttendanceRecordDto>.Validation("Attendance cannot be recorded for a future date");

        if (request.CheckInAt.HasValue && request.CheckOutAt.HasValue && request.CheckOutAt.Value < request.CheckInAt.Value)
            return Result<AttendanceRecordDto>.Validation("Check-out time cannot be before check-in time");

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Length > 1000)
            return Result<AttendanceRecordDto>.Validation("Notes cannot exceed 1000 characters");

        var now = DateTime.UtcNow;
        AttendanceRecord? record = null;
        if (request.Id.HasValue)
        {
            record = await _context.AttendanceRecords
                .FirstOrDefaultAsync(r => r.Id == request.Id.Value && r.FarmId == farmId);
            if (record == null)
                return Result<AttendanceRecordDto>.NotFound("Attendance record not found", DomainMessageKeys.AttendanceRecordNotFound);
        }

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

        record.EmployeeId = request.EmployeeId;
        record.Date = date;
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
            .ThenByDescending(r => r.CheckInAt)
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
            return Result.NotFound("Attendance record not found", DomainMessageKeys.AttendanceRecordNotFound);

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
