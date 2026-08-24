using FMS.Application.Common;

namespace FMS.Application.Attendance;

public interface IAttendanceService
{
    /// <summary>Creates today's record for the employee with CheckInAt = now.</summary>
    Task<Result<AttendanceRecordDto>> CheckInAsync(Guid farmId, Guid employeeId);

    /// <summary>Sets CheckOutAt = now on the employee's open record for today.</summary>
    Task<Result<AttendanceRecordDto>> CheckOutAsync(Guid farmId, Guid employeeId);

    /// <summary>Manual create/update of a day's record (admin corrections). One record per employee per date.</summary>
    Task<Result<AttendanceRecordDto>> UpsertAttendanceAsync(Guid farmId, UpsertAttendanceRequest request);

    Task<Result<PagedResult<AttendanceRecordDto>>> GetAttendanceAsync(Guid farmId, AttendanceListFilter filter);

    Task<Result> DeleteAttendanceAsync(Guid farmId, Guid id);
}
