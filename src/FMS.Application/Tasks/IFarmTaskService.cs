using FMS.Application.Common;

namespace FMS.Application.Tasks;

public interface IFarmTaskService
{
    Task<Result<FarmTaskDto>> GetTaskByIdAsync(Guid farmId, Guid id);
    /// <summary>
    /// The task list. With <see cref="FarmTaskListFilter.UpdatedSince"/> set this is a delta:
    /// only rows changed after that cursor, plus the ids of tasks deleted since (the audit log
    /// is where a hard-deleted row can still be named) and the server's own cursor for next
    /// time. A cursor that cannot be answered precisely sets <c>RequiresFullSync</c>.
    /// </summary>
    Task<Result<DeltaResult<FarmTaskDto>>> GetTasksAsync(Guid farmId, FarmTaskListFilter filter);
    Task<Result<FarmTaskDto>> CreateFarmTaskAsync(Guid farmId, CreateFarmTaskRequest request);
    Task<Result<FarmTaskDto>> UpdateFarmTaskAsync(Guid farmId, Guid id, UpdateFarmTaskRequest request);
    Task<Result> DeleteFarmTaskAsync(Guid farmId, Guid id);

    /// <summary>Moves a Pending task to InProgress.</summary>
    Task<Result<FarmTaskDto>> StartTaskAsync(Guid farmId, Guid id);

    /// <summary>
    /// Completes a Pending/InProgress task with optional notes, at the supplied device time
    /// when there is one.
    ///
    /// <para>
    /// The server owns the task's lifecycle, so a task that is not open cannot be completed:
    /// a Cancelled task is refused with the reason, and a task someone already completed is
    /// refused when the notes reported differ from the notes on record — that disagreement is a
    /// question for a person, not something to overwrite or discard. A completion that says the
    /// same thing as the recorded one (including a replay of the same queued mutation) is
    /// reported <see cref="Error.SupersededCode"/>: the intent is already satisfied, so a
    /// device may clear it from its queue.
    /// </para>
    /// </summary>
    /// <param name="mutationId">
    /// The queued mutation's id, stored on the task so a retry is refused by the database.
    /// Null for a live completion.
    /// </param>
    Task<Result<FarmTaskDto>> CompleteTaskAsync(Guid farmId, Guid id, CompleteFarmTaskRequest request, Guid? mutationId = null);

    /// <summary>Cancels a Pending/InProgress task with an optional reason.</summary>
    Task<Result<FarmTaskDto>> CancelTaskAsync(Guid farmId, Guid id, CancelFarmTaskRequest request);

    /// <summary>Returns a Completed/Cancelled task to Pending.</summary>
    Task<Result<FarmTaskDto>> ReopenTaskAsync(Guid farmId, Guid id);
}
