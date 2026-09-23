using FMS.Application.Common;

namespace FMS.Application.Attendance;

public interface IAttendanceService
{
    /// <summary>
    /// Creates the record for the employee with CheckInAt = now, or — when a device that was
    /// offline supplies <see cref="CheckInRequest.OccurredAt"/> — for the day that timestamp
    /// falls on, with CheckInAt = that timestamp.
    ///
    /// <para>
    /// The day is identified server-side (one row per employee per date) and its content has
    /// the device's answer for it: a queued time <em>earlier</em> than what is stored moves the
    /// stored check-in back, because a shift starts when the employee first arrived. A later
    /// one changes nothing and is reported <see cref="Error.SupersededCode"/> rather than as a
    /// failure, since the intent — "this employee checked in that day" — is already true.
    /// A day someone entered by hand with no check-in time is left untouched.
    /// </para>
    ///
    /// <para>
    /// Only a supplied device time can move a stored time. A live check-in supplies none (the
    /// server's clock says when the request arrived, not when the employee did), so an occupied
    /// day is refused exactly as it always was.
    /// </para>
    /// </summary>
    /// <param name="mutationId">
    /// The queued mutation's id, stored on the row it creates so a retry is refused by the
    /// database. Null for a live check-in.
    /// </param>
    Task<Result<AttendanceRecordDto>> CheckInAsync(Guid farmId, Guid employeeId, CheckInRequest? request = null, Guid? mutationId = null);

    /// <summary>
    /// Sets CheckOutAt = now (or the supplied device time) on the employee's open record for
    /// that day. Latest check-out wins: a later device time moves the stored one forward, and
    /// an earlier one is reported <see cref="Error.SupersededCode"/> and changes nothing. A
    /// live check-out on a day that is already closed stays the conflict it has always been,
    /// because its time is the server's clock rather than evidence about the shift.
    /// </summary>
    Task<Result<AttendanceRecordDto>> CheckOutAsync(Guid farmId, Guid employeeId, CheckOutRequest? request = null, Guid? mutationId = null);

    /// <summary>Manual create/update of a day's record (admin corrections). One record per employee per date.</summary>
    Task<Result<AttendanceRecordDto>> UpsertAttendanceAsync(Guid farmId, UpsertAttendanceRequest request);

    Task<Result<PagedResult<AttendanceRecordDto>>> GetAttendanceAsync(Guid farmId, AttendanceListFilter filter);

    Task<Result> DeleteAttendanceAsync(Guid farmId, Guid id);
}
