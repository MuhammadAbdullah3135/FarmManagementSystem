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

    /// <summary>
    /// The offline device's id for the mutation that created this row, when it arrived
    /// through the sync endpoint. Null for everything recorded live.
    ///
    /// Uniquely indexed per farm with <see cref="FarmId"/>. The <c>(EmployeeId, Date)</c>
    /// index already prevents two records for one employee-day; this one identifies
    /// <em>which</em> queued mutation produced the row that exists, which is what lets a
    /// replay return the original result instead of a conflict.
    /// </summary>
    public Guid? ClientMutationId { get; set; }

    public Employee Employee { get; set; } = null!;
}
