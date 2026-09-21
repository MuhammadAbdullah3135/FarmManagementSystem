using FMS.Application.Common;

namespace FMS.Application.Dashboard;

public interface IDashboardService
{
    Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid farmId);

    /// <summary>
    /// The dashboard page's alerts. Alerts whose inputs failed to compute are
    /// simply absent.
    /// </summary>
    Task<Result<List<DashboardAlertDto>>> GetAlertsAsync(Guid farmId);

    /// <summary>
    /// The same alerts, plus which alert types could not be evaluated. The
    /// notification dispatcher uses this instead of <see cref="GetAlertsAsync"/> so
    /// a failed metric cannot be mistaken for a cleared condition.
    /// </summary>
    Task<Result<DashboardAlertSetDto>> ComputeAlertSetAsync(Guid farmId);

    Task<Result<DashboardChartsDto>> GetChartsAsync(Guid farmId, DateTime? from, DateTime? to);
}
