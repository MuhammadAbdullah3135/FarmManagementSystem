using FMS.Domain.Common;
using FMS.Domain.Enums;

namespace FMS.Domain.Entities;

/// <summary>
/// A general-purpose farm task that can be assigned to an employee and optionally
/// linked to an animal or location. Distinct from auto-generated FeedingTasks.
/// </summary>
public class FarmTask : AuditableEntity
{
    public Guid FarmId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public FarmTaskPriority Priority { get; set; } = FarmTaskPriority.Medium;
    public FarmTaskStatus Status { get; set; } = FarmTaskStatus.Pending;
    public DateTime DueDate { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public string? CompletionNotes { get; set; }

    /// <summary>
    /// The offline device's id for the mutation that completed this task, when it arrived
    /// through the sync endpoint. Null for everything completed live.
    ///
    /// A completion changes a task in place and creates no row of its own, so the
    /// idempotency id has to live on the task itself. Uniquely indexed per farm with
    /// <see cref="FarmId"/>.
    /// </summary>
    public Guid? CompletionClientMutationId { get; set; }
    public string? CancelReason { get; set; }

    public Guid? AssignedEmployeeId { get; set; }
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }

    public Employee? AssignedEmployee { get; set; }
    public Animal? Animal { get; set; }
    public Location? Location { get; set; }
}
