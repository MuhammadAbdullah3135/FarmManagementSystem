using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

/// <summary>
/// One attendance record per employee per work session. An employee may have multiple
/// sessions in a day (for example breaks or split shifts), each with its own check-in
/// and check-out.
/// </summary>
public class AttendanceRecord : AuditableEntity
{
    public Guid FarmId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>The calendar day the session belongs to (time component ignored).</summary>
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The offline device's id for the mutation that created this row, when it arrived
    /// through the sync endpoint. Null for everything recorded live.
    ///
    /// Uniquely indexed per farm with <see cref="FarmId"/> so a replay returns the original
    /// result instead of creating a duplicate row.
    /// </summary>
    public Guid? ClientMutationId { get; set; }

    public Employee Employee { get; set; } = null!;
}
