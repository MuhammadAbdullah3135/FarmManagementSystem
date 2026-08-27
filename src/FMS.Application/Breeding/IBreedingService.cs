using FMS.Application.Common;

namespace FMS.Application.Breeding;

public interface IBreedingService
{
    // Breeding Records
    Task<Result<PagedResult<BreedingRecordDto>>> GetBreedingRecordsAsync(Guid farmId, BreedingRecordListFilter filter);
    Task<Result<BreedingRecordDto>> GetBreedingRecordByIdAsync(Guid farmId, Guid id);
    Task<Result<BreedingRecordDto>> CreateBreedingRecordAsync(Guid farmId, CreateBreedingRecordRequest request);
    Task<Result<BreedingRecordDto>> UpdateBreedingRecordAsync(Guid farmId, Guid id, UpdateBreedingRecordRequest request);
    Task<Result> DeleteBreedingRecordAsync(Guid farmId, Guid id);

    // Gestation Tracking
    Task<Result<PagedResult<GestationRecordDto>>> GetGestationRecordsAsync(Guid farmId, GestationRecordListFilter filter);
    Task<Result<GestationRecordDto>> GetGestationRecordByIdAsync(Guid farmId, Guid id);
    Task<Result<GestationRecordDto>> ConfirmPregnancyAsync(Guid farmId, ConfirmPregnancyRequest request);
    Task<Result> RevertPregnancyAsync(Guid farmId, Guid gestationRecordId, string reason);
    Task<Result<List<GestationHealthCheckDto>>> GetHealthChecksAsync(Guid farmId, Guid gestationRecordId);
    Task<Result<GestationHealthCheckDto>> LogHealthCheckAsync(Guid farmId, Guid gestationRecordId, LogGestationHealthCheckRequest request);

    // Birth Recording
    Task<Result<PagedResult<BirthRecordDto>>> GetBirthRecordsAsync(Guid farmId, BirthRecordListFilter filter);
    Task<Result<BirthRecordDto>> GetBirthRecordByIdAsync(Guid farmId, Guid id);
    Task<Result<BirthRecordDto>> CreateBirthRecordAsync(Guid farmId, CreateBirthRecordRequest request);
    Task<Result> DeleteBirthRecordAsync(Guid farmId, Guid id);

    // Lineage
    Task<Result<LineageResponse>> GetLineageAsync(Guid farmId, Guid animalId, int ancestorDepth, int descendantDepth);

    // Reports
    Task<Result<BreedingSummaryReport>> GetBreedingSummaryAsync(Guid farmId, BreedingReportFilter filter);
    Task<Result<List<BreedingTrendEntry>>> GetBreedingTrendAsync(Guid farmId, BreedingReportFilter filter);
    Task<Result<List<MethodDistributionEntry>>> GetMethodDistributionAsync(Guid farmId, BreedingReportFilter filter);
    Task<Result<List<SirePerformanceEntry>>> GetSirePerformanceAsync(Guid farmId, BreedingReportFilter filter);
    Task<Result<List<CalendarEventEntry>>> GetUpcomingCalendarEventsAsync(Guid farmId, int daysAhead);
}
