using FMS.Application.Common;

namespace FMS.Application.Performance;

public interface IPerformanceService
{
    Task<Result<PerformanceReviewDto>> GetReviewByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<PerformanceReviewDto>>> GetReviewsAsync(Guid farmId, PerformanceReviewListFilter filter);
    Task<Result<PerformanceReviewDto>> CreateReviewAsync(Guid farmId, CreatePerformanceReviewRequest request);
    Task<Result<PerformanceReviewDto>> UpdateReviewAsync(Guid farmId, Guid id, UpdatePerformanceReviewRequest request);
    Task<Result> DeleteReviewAsync(Guid farmId, Guid id);
}
