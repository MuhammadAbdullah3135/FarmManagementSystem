using FMS.Application.Common;

namespace FMS.Application.Reports;

/// <summary>
/// The cost-per-animal (and per-herd) report: costs and revenue attributed to each animal
/// in a date range, with every shared pool allocated by one stated basis.
/// </summary>
/// <remarks>
/// It lives beside the other reports rather than inside <see cref="IReportService"/>
/// because it depends on the feed and health cost services — it reuses their per-animal
/// figures rather than re-deriving them, so this report cannot disagree with theirs.
/// </remarks>
public interface ICostAttributionService
{
    Task<Result<CostPerAnimalReportDto>> GetCostPerAnimalReportAsync(Guid farmId, CostReportFilter filter);
}
