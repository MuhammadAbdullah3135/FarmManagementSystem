using FMS.Application.Common;

namespace FMS.Application.Health;

public interface IWeightCheckScheduleService
{
    Task<Result<PagedResult<WeightCheckScheduleDto>>> GetSchedulesAsync(Guid farmId, int page, int pageSize);
    Task<Result<WeightCheckScheduleDto>> CreateScheduleAsync(Guid farmId, CreateWeightCheckScheduleRequest request);
    Task<Result<WeightCheckScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateWeightCheckScheduleRequest request);
    Task<Result> DeleteScheduleAsync(Guid farmId, Guid id);
    Task<Result<List<WeightCheckStatusDto>>> GetWeightCheckStatusAsync(Guid farmId);
    Task<Result<List<WeightCheckStatusDto>>> GetOverdueWeightChecksAsync(Guid farmId);
}
