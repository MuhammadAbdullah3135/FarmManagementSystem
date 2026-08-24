using FMS.Application.Common;

namespace FMS.Application.Tasks;

public interface IFarmTaskService
{
    Task<Result<FarmTaskDto>> GetTaskByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<FarmTaskDto>>> GetTasksAsync(Guid farmId, FarmTaskListFilter filter);
    Task<Result<FarmTaskDto>> CreateFarmTaskAsync(Guid farmId, CreateFarmTaskRequest request);
    Task<Result<FarmTaskDto>> UpdateFarmTaskAsync(Guid farmId, Guid id, UpdateFarmTaskRequest request);
    Task<Result> DeleteFarmTaskAsync(Guid farmId, Guid id);

    /// <summary>Moves a Pending task to InProgress.</summary>
    Task<Result<FarmTaskDto>> StartTaskAsync(Guid farmId, Guid id);

    /// <summary>Completes a Pending/InProgress task with optional notes.</summary>
    Task<Result<FarmTaskDto>> CompleteTaskAsync(Guid farmId, Guid id, CompleteFarmTaskRequest request);

    /// <summary>Cancels a Pending/InProgress task with an optional reason.</summary>
    Task<Result<FarmTaskDto>> CancelTaskAsync(Guid farmId, Guid id, CancelFarmTaskRequest request);

    /// <summary>Returns a Completed/Cancelled task to Pending.</summary>
    Task<Result<FarmTaskDto>> ReopenTaskAsync(Guid farmId, Guid id);
}
