using FMS.Application.Common;

namespace FMS.Application.Health;

public interface IWeightCheckScheduleService
{
    Task<Result<PagedResult<WeightCheckScheduleDto>>> GetSchedulesAsync(Guid farmId, int page, int pageSize);
    Task<Result<WeightCheckScheduleDto>> CreateScheduleAsync(Guid farmId, CreateWeightCheckScheduleRequest request);
    Task<Result<WeightCheckScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateWeightCheckScheduleRequest request);
    Task<Result> DeleteScheduleAsync(Guid farmId, Guid id);
    /// <summary>The whole projection. Used by the dashboard and the scheduled jobs.</summary>
    Task<Result<List<WeightCheckStatusDto>>> GetWeightCheckStatusAsync(Guid farmId);

    /// <summary>
    /// The projection as a cached read. It is computed rather than stored, so there is no row
    /// timestamp to compare: "changed" means one of the entry's own inputs changed — the
    /// animal, a schedule that matches it, or its latest weight — and an animal that was
    /// deleted is reported as a tombstone id. A schedule that was edited or removed in the
    /// window makes the answer <c>RequiresFullSync</c>, because it can take entries away and a
    /// delta has no id for an entry that no longer exists.
    /// </summary>
    Task<Result<DeltaResult<WeightCheckStatusDto>>> GetWeightCheckStatusAsync(Guid farmId, DateTime? updatedSince);

    Task<Result<List<WeightCheckStatusDto>>> GetOverdueWeightChecksAsync(Guid farmId);
}
