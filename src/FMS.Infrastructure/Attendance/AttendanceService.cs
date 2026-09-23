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
            return Result<AttendanceRecordDto>.NotFound("Employee not found");

        // A queued check-in carries the time the device was at the gate, so the record belongs
        // to that day rather than to the day the sync happened to reach the server. A live
        // check-in supplies nothing and lands on today, exactly as before.
        var now = DateTime.UtcNow;
        var occurredAt = request?.OccurredAt ?? now;
        if (MutationTimestampRules.IsTooFarInTheFuture(occurredAt, now))
            return Result<AttendanceRecordDto>.Validation(MutationTimestampRules.CheckInOccurredAtMessage);

        // Identity is the server's — one row per (employee, day), however many devices report
        // it — while content is the device's. So an occupied day is not automatically a
        // refusal: the queued time is compared against what the row already says.
        //
        // Matched on (employee, date) with no farm filter, exactly as the pre-check this
        // replaced did: the unique index is on that pair, so a narrower query could find
        // nothing while the index still refuses the insert.
        var date = occurredAt.Date;
        var deviceTime = request?.OccurredAt;
        var existing = await _context.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.Date == date);

        if (existing is null)
        {
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
        // arrived, not when the employee did — so an occupied day simply stands, which is this
        // endpoint's original behaviour down to the wording.
        if (deviceTime is null)
            return Result<AttendanceRecordDto>.Conflict(
                "Employee already has an attendance record for that day");

        // A record with no check-in time is somebody's deliberate manual entry (a Leave, an
        // Absent) rather than a shift start, so there is no time for "earliest wins" to beat.
        // It is left exactly as it is: no clock overturns what a person typed, and the manual
        // endpoint can still change the day.
        if (existing.CheckInAt is null)
            return Result<AttendanceRecordDto>.Superseded(
                $"That day already has an attendance record marked {existing.Status}; it was not changed");

        // Earliest check-in wins: a shift's start is the first time an employee was at the
        // gate, so an earlier queued time moves it back and a later one leaves it alone. The
        // later case is "already satisfied", not an error — the day does have a check-in.
        if (deviceTime.Value >= existing.CheckInAt.Value)
            return Result<AttendanceRecordDto>.Superseded(
                "Employee already has an attendance record for that day");

        existing.CheckInAt = occurredAt;

        // The row keeps the id of the queued mutation that created it; a rewind of a row a
        // live request created stamps this one, so a crash between the apply and the ledger
        // row is still caught by the row's own unique index on the next attempt.
        existing.ClientMutationId ??= mutationId;
        existing.ModifiedAt = now;
        existing.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<AttendanceRecordDto>.Success(MapRecord(existing, employee));
    }

    public async Task<Result<AttendanceRecordDto>> CheckOutAsync(
        Guid farmId, Guid employeeId, CheckOutRequest? request = null, Guid? mutationId = null)
    {
        var now = DateTime.UtcNow;
        var deviceTime = request?.OccurredAt;
        var occurredAt = deviceTime ?? now;
        if (MutationTimestampRules.IsTooFarInTheFuture(occurredAt, now))
            return Result<AttendanceRecordDto>.Validation(MutationTimestampRules.CheckOutOccurredAtMessage);

        var record = await _context.AttendanceRecords
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.FarmId == farmId &&
                                      r.Date == occurredAt.Date);
        if (record == null)
            return Result<AttendanceRecordDto>.NotFound("No attendance record found for today");

        if (record.CheckOutAt.HasValue)
        {
            // Nothing to compare: without a device's time there is no evidence about when the
            // shift ended, so a live check-out on a day that is already closed is the same
            // conflict it has always been.
            if (deviceTime is null)
                return Result<AttendanceRecordDto>.Conflict("Employee has already checked out today");

            // Latest check-out wins — the mirror of the check-in rule. The row records when the
            // shift actually ended, so a device reporting a later time moves it forward rather
            // than being refused, and an earlier one leaves the later time standing.
            if (deviceTime.Value <= record.CheckOutAt.Value)
                return Result<AttendanceRecordDto>.Superseded("Employee has already checked out today");
        }

        if (occurredAt < record.CheckInAt)
            return Result<AttendanceRecordDto>.Validation("Check-out time cannot be before check-in time");

        record.CheckOutAt = occurredAt;

        // The row keeps the id of the first queued mutation that wrote it: a check-out does not
        // displace the check-in that created the day's record. Both mutations are recorded in
        // the ledger, which is what actually answers a replay.
        record.ClientMutationId ??= mutationId;

        record.ModifiedAt = now;
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
