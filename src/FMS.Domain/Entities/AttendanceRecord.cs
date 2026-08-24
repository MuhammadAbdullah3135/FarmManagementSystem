using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

/// <summary>One attendance record per employee per day.</summary>
public class AttendanceRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The calendar day this record covers (time component ignored).</summary>
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }
    public string? Notes { get; set; }

    public Employee Employee { get; set; } = null!;
}
