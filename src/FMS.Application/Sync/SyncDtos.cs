using System.Text.Json;
using FMS.Application.Common;

namespace FMS.Application.Sync;

/// <summary>
/// What happened to one queued mutation.
///
/// The three outcomes exist because a background queue cannot answer with a status code:
/// one item's failure must not fail the batch, and "nothing to do because it was already
/// done" is not the same as "refused".
/// </summary>
public enum SyncMutationOutcome
{
    /// <summary>The workflow ran and its effect is committed.</summary>
    Accepted,

    /// <summary>
    /// The workflow refused the change but the intent is already satisfied by existing state,
    /// so the device may treat the item as done. Today that is an attendance conflict: the
    /// employee-day already has a record, and 4.5's policy is that the earliest check-in wins.
    /// </summary>
    Superseded,

    /// <summary>Nothing was written and the server's own message explains why.</summary>
    Rejected
}

/// <summary>
/// A batch of mutations a device applied while it was offline.
/// </summary>
public class SyncMutationRequest
{
    /// <summary>
    /// Transport guard, not the queue-cap policy (still an open question in the offline
    /// architecture): a request this large is malformed, and refusing it costs one round trip
    /// instead of applying half a queue whose answer was lost.
    /// </summary>
    public const int MaxItemsPerRequest = 200;

    public List<SyncMutationItem> Items { get; set; } = new();
}

/// <summary>
/// One queued mutation. The payload is read per operation rather than model-bound, so a
/// payload the server cannot understand rejects that item alone instead of the whole batch.
/// </summary>
public class SyncMutationItem
{
    /// <summary>One of <see cref="SyncOperations"/>.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>The device's id for this mutation. Repeating it is what makes a retry safe.</summary>
    public Guid ClientMutationId { get; set; }

    public JsonElement Payload { get; set; }
}

public class SyncMutationItemResult
{
    /// <summary>
    /// The item's position in <em>this</em> request. The value stored with the original result
    /// is overwritten on a replay, so a re-sent batch reports where the item sits now.
    /// </summary>
    public int Index { get; set; }

    public Guid ClientMutationId { get; set; }

    public SyncMutationOutcome Outcome { get; set; }

    /// <summary>The server's own message, verbatim (see the parity tests). Null when accepted.</summary>
    public string? Message { get; set; }

    /// <summary>The row the mutation created or changed, when the operation has one.</summary>
    public Guid? TargetEntityId { get; set; }

    /// <summary>
    /// The workflow's own result value, exactly as the single-record endpoint returns it. On a
    /// replay this is the stored copy, not a recomputed one.
    /// </summary>
    public JsonElement? Result { get; set; }
}

/// <summary>
/// The batch answer. <see cref="RequestedCount"/>, <see cref="SuccessCount"/> and
/// <see cref="Failures"/> keep the shape bulk import (3.4/4.3) already established, so a client
/// reads both the same way; the item list is the extra detail a queue needs to quarantine a
/// single bad row.
/// </summary>
public class SyncMutationResultDto
{
    public int RequestedCount { get; set; }

    /// <summary>Items the device may treat as done: accepted plus superseded.</summary>
    public int SuccessCount { get; set; }

    public int AcceptedCount { get; set; }

    public int SupersededCount { get; set; }

    public int RejectedCount { get; set; }

    public List<SyncMutationItemResult> Items { get; set; } = new();

    /// <summary>One entry per rejected item, by position — the import shape, unchanged.</summary>
    public List<BulkCreateFailureDto> Failures { get; set; } = new();
}

// ── per-operation payloads ──────────────────────────────────

public class WeightRecordMutation
{
    public Guid AnimalId { get; set; }
    public decimal WeightKg { get; set; }

    /// <summary>When the weight was taken. Absent means the server stamps its own clock.</summary>
    public DateTime? RecordedAt { get; set; }

    public string? Notes { get; set; }
}

public class AttendanceMutation
{
    public Guid EmployeeId { get; set; }
    public DateTime? OccurredAt { get; set; }
}

public class TaskCompletionMutation
{
    public Guid TaskId { get; set; }
    public string? CompletionNotes { get; set; }
    public DateTime? OccurredAt { get; set; }
}
