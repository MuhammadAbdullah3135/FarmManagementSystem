using FMS.Domain.Enums;

namespace FMS.Application.Attendance;

public class AttendanceRecordDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }

    /// <summary>Hours between check-in and check-out, rounded to 2 decimals. Null when either is missing.</summary>
    public decimal? HoursWorked { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Body of a check-in. Optional: the live endpoint sends no body and the server stamps the
/// time, while a device that was offline supplies the time the work actually happened.
/// </summary>
public class CheckInRequest
{
    public DateTime? OccurredAt { get; set; }
}

/// <summary>Body of a check-out; same contract as <see cref="CheckInRequest"/>.</summary>
public class CheckOutRequest
{
    public DateTime? OccurredAt { get; set; }
}

public class UpsertAttendanceRequest
{
    public Guid EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }
    public string? Notes { get; set; }
}

public class AttendanceListFilter
{
    public Guid? EmployeeId { get; set; }
    public AttendanceStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
