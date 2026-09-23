using System.Text.Json;
using System.Text.Json.Serialization;
using FMS.Application.Animal;
using FMS.Application.Attendance;
using FMS.Application.Common;
using FMS.Application.Sync;
using FMS.Application.Tasks;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FMS.Infrastructure.Sync;

/// <summary>
/// Applies queued offline mutations, one item at a time, by calling the workflow's own
/// service method.
///
/// <para>
/// Three properties this class exists to guarantee, in this order:
/// </para>
/// <list type="number">
/// <item>
/// <b>Validation cannot drift.</b> Every item runs through the same
/// <c>AddWeightAsync</c>/<c>CheckInAsync</c>/<c>CheckOutAsync</c>/<c>CompleteTaskAsync</c> a
/// live request runs, with the route's farm id — so the same input gets the same message, and
/// an item naming another farm's animal is refused by the same check that refuses a forged
/// request. No rule is restated here.
/// </item>
/// <item>
/// <b>A retry applies once and answers with the original result.</b> The
/// <c>ProcessedMutation</c> ledger is consulted first and its stored result is replayed
/// verbatim. The mutation id also sits on the row the workflow created, so the database itself
/// refuses a duplicate even when two syncs race past the lookup.
/// </item>
/// <item>
/// <b>One bad item never blocks the queue.</b> Each item runs in its own DI scope, so a failed
/// item's half-tracked <c>DbContext</c> is discarded before the next one is applied — the
/// difference between quarantining a row and stalling the device.
/// </item>
/// </list>
///
/// <para>
/// Write order is deliberate: the workflow commits first, then the ledger row. Recording first
/// would let a crash leave a ledger entry for a mutation that was never applied, and a replay
/// would then answer "done" for work that does not exist — silent data loss. Applying first can
/// only produce the opposite: applied but unrecorded, which the target row's own unique index
/// catches on the next attempt (see <see cref="AlreadyAppliedAsync"/>).
/// </para>
/// </summary>
public class MutationSyncService : IMutationSyncService
{
    /// <summary>
    /// Web defaults (camelCase, case-insensitive) because payloads arrive from a browser — the
    /// same shape the API's own JSON options accept — plus the string enum converter so a
    /// stored outcome reads back as <c>"accepted"</c> rather than <c>0</c>.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MutationSyncService> _logger;

    public MutationSyncService(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MutationSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<SyncMutationResultDto> ApplyAsync(
        Guid farmId, SyncMutationRequest request, CancellationToken cancellationToken = default)
    {
        var batch = new SyncMutationResultDto { RequestedCount = request.Items.Count };

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];

            SyncMutationItemResult itemResult;
            try
            {
                itemResult = await ApplyItemAsync(farmId, item, index, cancellationToken);
            }
            catch (Exception ex)
            {
                // ApplyItemAsync isolates per item, so reaching here means something outside its
                // own guarantees failed. The batch still answers: one item's fault is not the
                // queue's, and the exception text is logged rather than returned to the device.
                _logger.LogError(ex,
                    "Sync item {Index} ({Operation}) failed unexpectedly for farm {FarmId}",
                    index, item?.Operation, farmId);
                itemResult = Rejected(index, item?.ClientMutationId ?? Guid.Empty,
                    "The item could not be applied on the server");
            }

            batch.Items.Add(itemResult);

            switch (itemResult.Outcome)
            {
                case SyncMutationOutcome.Accepted: batch.AcceptedCount++; break;
                case SyncMutationOutcome.Superseded: batch.SupersededCount++; break;
                default:
                    batch.RejectedCount++;
                    batch.Failures.Add(new BulkCreateFailureDto
                    {
                        Index = itemResult.Index,
                        Message = itemResult.Message ?? "The item was rejected"
                    });
                    break;
            }
        }

        // Accepted plus superseded: the two outcomes a device may clear from its queue.
        batch.SuccessCount = batch.AcceptedCount + batch.SupersededCount;
        return batch;
    }

    private async Task<SyncMutationItemResult> ApplyItemAsync(
        Guid farmId, SyncMutationItem? item, int index, CancellationToken cancellationToken)
    {
        if (item is null)
            return Rejected(index, Guid.Empty, "An item is missing from the request");

        if (item.ClientMutationId == Guid.Empty)
            return Rejected(index, Guid.Empty, "A client mutation id is required");

        if (!SyncOperations.IsKnown(item.Operation))
            return Rejected(index, item.ClientMutationId, $"Unknown operation '{item.Operation}'");

        var requiredRoles = SyncOperations.RequiredRoles(item.Operation);
        if (requiredRoles is { Count: > 0 } &&
            !requiredRoles.Any(role => _httpContextAccessor.HttpContext?.User.IsInRole(role) == true))
            return Rejected(index, item.ClientMutationId, "You do not have permission to perform this action");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();

        var recorded = await db.ProcessedMutations
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.FarmId == farmId && m.ClientMutationId == item.ClientMutationId,
                cancellationToken);

        if (recorded is not null)
            return Replay(recorded, index, item.ClientMutationId);

        WorkflowOutcome applied;
        try
        {
            applied = await ApplyWorkflowAsync(scope.ServiceProvider, farmId, item, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsClientMutationIdConflict(ex))
        {
            return await AlreadyAppliedAsync(db, farmId, item, index, cancellationToken);
        }

        // Only an applied mutation is recorded. A rejection wrote nothing, and a superseded item
        // wrote nothing either — its intent was already satisfied by state someone else created —
        // so neither is "processed": re-sending them re-derives the same answer for as long as the
        // state that produced it stands, and can still apply if that state is undone.
        return applied.Outcome switch
        {
            SyncMutationOutcome.Rejected => Rejected(index, item.ClientMutationId, applied.Message),
            SyncMutationOutcome.Superseded => Superseded(index, item.ClientMutationId, applied.Message),
            _ => await RecordAsync(db, farmId, item, index, applied, cancellationToken)
        };
    }

    /// <summary>
    /// Runs one item's workflow through the service that owns it. Nothing about validation,
    /// farm scoping or messages is reimplemented here — only the payload is read and the result
    /// classified.
    /// </summary>
    private static async Task<WorkflowOutcome> ApplyWorkflowAsync(
        IServiceProvider provider, Guid farmId, SyncMutationItem item, CancellationToken cancellationToken)
    {
        switch (item.Operation)
        {
            case SyncOperations.WeightRecord:
            {
                var payload = ReadPayload<WeightRecordMutation>(item);
                if (payload is null)
                    return WorkflowOutcome.Rejected("The payload could not be read as a weight record");

                var request = new CreateWeightRecordRequest
                {
                    WeightKg = payload.WeightKg,
                    RecordedAt = payload.RecordedAt ?? default,
                    Notes = payload.Notes
                };

                var result = await provider.GetRequiredService<IAnimalService>()
                    .AddWeightAsync(farmId, payload.AnimalId, request, item.ClientMutationId);

                // Append-only by design: two measurements are two facts, and a weight has no
                // conflict path at all.
                return WorkflowOutcome.From(result, dto => dto.Id, payload.RecordedAt, conflictIsSuperseded: false);
            }

            case SyncOperations.AttendanceCheckIn:
            {
                var payload = ReadPayload<AttendanceMutation>(item);
                if (payload is null)
                    return WorkflowOutcome.Rejected("The payload could not be read as a check-in");

                var result = await provider.GetRequiredService<IAttendanceService>()
                    .CheckInAsync(farmId, payload.EmployeeId,
                        new CheckInRequest { OccurredAt = payload.OccurredAt }, item.ClientMutationId);

                // Earliest check-in wins (4.5's policy): the employee-day already has a record,
                // so the later one is superseded rather than an error the user must resolve.
                return WorkflowOutcome.From(result, dto => dto.Id, payload.OccurredAt, conflictIsSuperseded: true);
            }

            case SyncOperations.AttendanceCheckOut:
            {
                var payload = ReadPayload<AttendanceMutation>(item);
                if (payload is null)
                    return WorkflowOutcome.Rejected("The payload could not be read as a check-out");

                var result = await provider.GetRequiredService<IAttendanceService>()
                    .CheckOutAsync(farmId, payload.EmployeeId,
                        new CheckOutRequest { OccurredAt = payload.OccurredAt }, item.ClientMutationId);

                // "Already checked out" means the intent is satisfied; a check-out with no
                // record for that day is a genuine rejection the user has to fix.
                return WorkflowOutcome.From(result, dto => dto.Id, payload.OccurredAt, conflictIsSuperseded: true);
            }

            case SyncOperations.TaskComplete:
            {
                var payload = ReadPayload<TaskCompletionMutation>(item);
                if (payload is null)
                    return WorkflowOutcome.Rejected("The payload could not be read as a task completion");

                var result = await provider.GetRequiredService<IFarmTaskService>()
                    .CompleteTaskAsync(farmId, payload.TaskId,
                        new CompleteFarmTaskRequest
                        {
                            CompletionNotes = payload.CompletionNotes,
                            OccurredAt = payload.OccurredAt
                        },
                        item.ClientMutationId);

                // Completion is a transition, not a value, and the server owns the lifecycle:
                // a Cancelled task refuses the completion with its own message, and a task
                // someone completed with different notes refuses it as a disagreement for a
                // human. A completion whose intent the task's state already satisfies (it is
                // complete, with the same notes, or by this same mutation) comes back as
                // superseded — the service says which of the two it is, and `false` here keeps
                // a plain conflict a rejection for this workflow.
                return WorkflowOutcome.From(result, dto => dto.Id, payload.OccurredAt, conflictIsSuperseded: false);
            }

            default:
                return WorkflowOutcome.Rejected($"Unknown operation '{item.Operation}'");
        }
    }

    /// <summary>
    /// Stores the result a replay will be answered with. The stored record is the item result
    /// itself, so a retry gets the same outcome, message, target id and payload the first
    /// attempt returned.
    ///
    /// <para>
    /// One caller: an accepted item. (The crash-window path in
    /// <see cref="AlreadyAppliedAsync"/> records a row of its own, because there the target row
    /// already carries the mutation id and every retry would otherwise hit the database's refusal
    /// again.)
    /// </para>
    /// </summary>
    private async Task<SyncMutationItemResult> RecordAsync(
        FmsDbContext db, Guid farmId, SyncMutationItem item, int index, WorkflowOutcome applied,
        CancellationToken cancellationToken)
    {
        var itemResult = new SyncMutationItemResult
        {
            Index = index,
            ClientMutationId = item.ClientMutationId,
            Outcome = applied.Outcome,
            Message = applied.Message,
            TargetEntityId = applied.TargetEntityId,
            Result = applied.Value is null
                ? null
                : JsonSerializer.SerializeToElement(applied.Value, applied.Value.GetType(), SerializerOptions)
        };

        db.ProcessedMutations.Add(BuildLedgerRow(farmId, item, applied, itemResult));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsClientMutationIdConflict(ex))
        {
            // Another sync recorded this mutation while this one was applying it. Its row is the
            // truth; this one disappears and the answer comes from the stored result.
            db.ChangeTracker.Clear();

            var recorded = await db.ProcessedMutations
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.FarmId == farmId && m.ClientMutationId == item.ClientMutationId,
                    cancellationToken);

            return recorded is not null
                ? Replay(recorded, index, item.ClientMutationId)
                : Superseded(index, item.ClientMutationId, AlreadyAppliedMessage);
        }

        return itemResult;
    }

    /// <summary>
    /// The crash window, handled honestly: the row exists carrying this mutation id but the
    /// ledger has no entry, so a previous attempt applied the mutation and died before recording
    /// its result. The database has already refused the second apply, which is the property that
    /// matters — no duplicate record exists. The answer repeats what the state is, and recording
    /// it now makes every later retry identical instead of re-attempting the apply.
    /// </summary>
    private async Task<SyncMutationItemResult> AlreadyAppliedAsync(
        FmsDbContext db, Guid farmId, SyncMutationItem item, int index, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        var itemResult = Superseded(index, item.ClientMutationId, AlreadyAppliedMessage);
        db.ProcessedMutations.Add(BuildLedgerRow(
            farmId, item, WorkflowOutcome.Superseded(AlreadyAppliedMessage, null, OccurredAtOf(item)), itemResult));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Whoever won the race owns the recorded answer; the item is still applied once,
            // which is what the device needs to know, and that is what it is being told.
        }

        return itemResult;
    }

    private const string AlreadyAppliedMessage =
        "This mutation was already applied on the server; the original response was not recorded";

    private ProcessedMutation BuildLedgerRow(
        Guid farmId, SyncMutationItem item, WorkflowOutcome applied, SyncMutationItemResult itemResult) =>
        new()
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            ClientMutationId = item.ClientMutationId,
            Operation = item.Operation,
            TargetEntityId = applied.TargetEntityId,
            // Stored as the enum name, which is also what the response carries: the API's
            // JsonStringEnumConverter writes enum names unchanged everywhere else.
            Outcome = applied.Outcome.ToString(),
            ResultJson = JsonSerializer.Serialize(itemResult, SerializerOptions),
            OccurredAt = applied.OccurredAt,
            CreatedAt = DateTime.UtcNow
        };

    /// <summary>
    /// An already-processed mutation is answered from the ledger, so a retry returns the original
    /// result rather than a fresh attempt whose outcome could differ. Only the position and the
    /// echoed id are taken from this request.
    /// </summary>
    private static SyncMutationItemResult Replay(ProcessedMutation recorded, int index, Guid clientMutationId)
    {
        SyncMutationItemResult? stored = null;
        try
        {
            stored = JsonSerializer.Deserialize<SyncMutationItemResult>(recorded.ResultJson, SerializerOptions);
        }
        catch (JsonException)
        {
            // Nothing in normal operation writes this, but a result that cannot be read must not
            // turn a retry into a 500: fall through to the conservative answer below.
        }

        if (stored is null)
        {
            var outcome = Enum.TryParse<SyncMutationOutcome>(recorded.Outcome, ignoreCase: true, out var parsed)
                ? parsed
                : SyncMutationOutcome.Accepted;

            return new SyncMutationItemResult
            {
                Index = index,
                ClientMutationId = clientMutationId,
                Outcome = outcome,
                Message = AlreadyAppliedMessage,
                TargetEntityId = recorded.TargetEntityId
            };
        }

        stored.Index = index;
        stored.ClientMutationId = clientMutationId;
        return stored;
    }

    private static T? ReadPayload<T>(SyncMutationItem item)
    {
        if (item.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return default;

        try
        {
            return item.Payload.Deserialize<T>(SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static DateTime? OccurredAtOf(SyncMutationItem item) => item.Operation switch
    {
        SyncOperations.WeightRecord => ReadPayload<WeightRecordMutation>(item)?.RecordedAt,
        SyncOperations.TaskComplete => ReadPayload<TaskCompletionMutation>(item)?.OccurredAt,
        _ => ReadPayload<AttendanceMutation>(item)?.OccurredAt
    };

    private static SyncMutationItemResult Rejected(int index, Guid clientMutationId, string? message) =>
        new()
        {
            Index = index,
            ClientMutationId = clientMutationId,
            Outcome = SyncMutationOutcome.Rejected,
            Message = message ?? "The item was rejected"
        };

    private static SyncMutationItemResult Superseded(int index, Guid clientMutationId, string? message) =>
        new()
        {
            Index = index,
            ClientMutationId = clientMutationId,
            Outcome = SyncMutationOutcome.Superseded,
            Message = message
        };

    /// <summary>
    /// True when a failed save was refused by one of the idempotency unique indexes — either the
    /// ledger's or a target row's. Any other constraint violation is a real conflict the caller
    /// should see as a rejection, not as "already applied", so the constraint name is checked
    /// rather than the error code alone.
    ///
    /// <para>
    /// Provider-specific by necessity: this is the one place the code reaches for PostgreSQL, and
    /// it is reachable only when a unique index actually fires. The in-memory provider used by the
    /// unit tests ignores unique indexes entirely, which is why the race path is proven by the
    /// PostgreSQL-gated test rather than by a unit test.
    /// </para>
    /// </summary>
    private static bool IsClientMutationIdConflict(DbUpdateException exception) =>
        FindPostgresException(exception) is { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        (postgres.ConstraintName?.Contains("ClientMutationId", StringComparison.Ordinal) == true ||
         postgres.MessageText.Contains("ClientMutationId", StringComparison.Ordinal));

    private static PostgresException? FindPostgresException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is PostgresException postgres)
                return postgres;

            exception = exception.InnerException;
        }

        return null;
    }

    /// <summary>The result of one workflow call, already classified for the device.</summary>
    private readonly record struct WorkflowOutcome(
        SyncMutationOutcome Outcome,
        string? Message,
        Guid? TargetEntityId,
        object? Value,
        DateTime? OccurredAt)
    {
        public static WorkflowOutcome Rejected(string message) =>
            new(SyncMutationOutcome.Rejected, message, null, null, null);

        public static WorkflowOutcome Superseded(string message, Guid? targetEntityId, DateTime? occurredAt) =>
            new(SyncMutationOutcome.Superseded, message, targetEntityId, null, occurredAt);

        /// <summary>
        /// Classifies a workflow result: success is accepted, and a failure is either
        /// superseded (existing state already means what the item asked for, so the device may
        /// treat it as done) or rejected (the server refuses the change, and the device keeps
        /// the item with the server's own message).
        ///
        /// <para>
        /// <see cref="Error.SupersededCode"/> is the workflow saying so itself, which is what
        /// the already-satisfied cases added in 4.5.5 use — a completion for a task that is
        /// already complete, for instance. <paramref name="conflictIsSuperseded"/> remains the
        /// fallback for a workflow whose declared strategy makes a plain conflict the
        /// satisfied case (a check-in for a day that already has an earlier one). Neither is
        /// decided by reading the message, so rewording a message cannot change an outcome.
        /// </para>
        /// </summary>
        public static WorkflowOutcome From<T>(
            Result<T> result,
            Func<T, Guid> targetEntityId,
            DateTime? occurredAt,
            bool conflictIsSuperseded)
        {
            if (result.IsSuccess)
                return new WorkflowOutcome(
                    SyncMutationOutcome.Accepted, null, targetEntityId(result.Value!), result.Value, occurredAt);

            var error = result.Error!;
            var outcome = error.Code == Error.SupersededCode ||
                          (conflictIsSuperseded && error.Code == "Conflict")
                ? SyncMutationOutcome.Superseded
                : SyncMutationOutcome.Rejected;

            return new WorkflowOutcome(outcome, error.Message, null, null, occurredAt);
        }
    }
}
