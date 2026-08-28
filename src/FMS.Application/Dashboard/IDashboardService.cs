using FMS.Application.Common;

namespace FMS.Application.Dashboard;

public interface IDashboardService
{
    Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid farmId);
    Task<Result<List<DashboardAlertDto>>> GetAlertsAsync(Guid farmId);
    Task<Result<DashboardChartsDto>> GetChartsAsync(Guid farmId, DateTime? from, DateTime? to);
}
