using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// One client-queued mutation the server has applied, with the result it returned.
///
/// This is the idempotency ledger: a device that retries after an ambiguous failure
/// (the response was lost, the connection dropped mid-sync) must not apply the same
/// mutation twice, and must get the <em>original</em> answer back rather than a fresh
/// one — otherwise a retried weight could be reported as a different row, and the
/// client could not reconcile what it already showed the user.
///
/// The row is unique on <c>(FarmId, ClientMutationId)</c>, so uniqueness is scoped to the
/// farm: a lookup can never reach another farm's stored result even in principle. The
/// same id is also stamped onto the row the mutation created (see
/// <see cref="WeightRecord.ClientMutationId"/> and friends), which is what makes the
/// database itself refuse a concurrent duplicate apply.
///
/// Bookkeeping, not business data: the records this row describes are audited on their
/// own, and a ledger row has nothing about it a user would want in the audit trail.
/// </summary>
public class ProcessedMutation : AuditableEntity, IAuditLogExcluded
{
    public Guid FarmId { get; set; }

    /// <summary>The operation id the device generated for this mutation.</summary>
    public Guid ClientMutationId { get; set; }

    /// <summary>Which workflow applied it — one of <c>SyncOperations</c>.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>The row the mutation created or changed, when the operation has one.</summary>
    public Guid? TargetEntityId { get; set; }

    /// <summary><c>accepted</c> or <c>superseded</c> — a rejected item writes nothing and is not recorded.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// The item result exactly as it was returned the first time, so a replay answers
    /// byte-for-byte rather than re-running the workflow.
    /// </summary>
    public string ResultJson { get; set; } = string.Empty;

    /// <summary>The device-supplied time of the mutation, where the workflow carries one.</summary>
    public DateTime? OccurredAt { get; set; }

    public Farm Farm { get; set; } = null!;
}
