using FMS.Application.Common;

namespace FMS.Application.Health;

public interface IHealthCostService
{
    Task<Result<HealthCostSummaryDto>> GetCostSummaryAsync(Guid farmId, HealthCostFilter filter);
    Task<Result<List<HealthCostByVetDto>>> GetCostsByVetAsync(Guid farmId, HealthCostFilter filter);
    Task<Result<List<HealthCostByAnimalDto>>> GetCostsByAnimalAsync(Guid farmId, HealthCostFilter filter);
    Task<Result<List<HealthCostByMonthDto>>> GetCostsByMonthAsync(Guid farmId, int year);
}
