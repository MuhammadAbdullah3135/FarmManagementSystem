using FMS.Application.Common;

namespace FMS.Application.Feed;

public interface IFeedService
{
    // Feed types
    Task<Result<List<FeedTypeDto>>> GetFeedTypesAsync(Guid farmId);
    Task<Result<FeedTypeDto>> GetFeedTypeByIdAsync(Guid farmId, Guid id);
    Task<Result<FeedTypeDto>> CreateFeedTypeAsync(Guid farmId, CreateFeedTypeRequest request);
    Task<Result<FeedTypeDto>> UpdateFeedTypeAsync(Guid farmId, Guid id, UpdateFeedTypeRequest request);
    Task<Result> DeleteFeedTypeAsync(Guid farmId, Guid id);

    // Inventory
    Task<Result<StockMovementDto>> RecordStockMovementAsync(Guid farmId, RecordStockMovementRequest request);
    Task<Result<PagedResult<StockMovementDto>>> GetStockMovementsAsync(Guid farmId, Guid? feedTypeId, int page, int pageSize);
    Task<Result<List<FeedStockDto>>> GetStockAsync(Guid farmId);
    Task<Result<FeedStockDto>> GetStockByFeedTypeAsync(Guid farmId, Guid feedTypeId);

    // Feed records
    Task<Result<FeedRecordDto>> CreateFeedRecordAsync(Guid farmId, CreateFeedRecordRequest request);
    Task<Result<FeedRecordDto>> GetFeedRecordByIdAsync(Guid farmId, Guid id);
    Task<Result<PagedResult<FeedRecordDto>>> GetFeedRecordsAsync(Guid farmId, FeedRecordListFilter filter);
    Task<Result<FeedRecordDto>> UpdateFeedRecordAsync(Guid farmId, Guid id, UpdateFeedRecordRequest request);
    Task<Result> DeleteFeedRecordAsync(Guid farmId, Guid id);

    // Diet plans
    Task<Result<List<DietPlanDto>>> GetDietPlansAsync(Guid farmId);
    Task<Result<DietPlanDto>> GetDietPlanByIdAsync(Guid farmId, Guid id);
    Task<Result<DietPlanDto>> CreateDietPlanAsync(Guid farmId, CreateDietPlanRequest request);
    Task<Result<DietPlanDto>> UpdateDietPlanAsync(Guid farmId, Guid id, UpdateDietPlanRequest request);
    Task<Result> DeleteDietPlanAsync(Guid farmId, Guid id);
    Task<Result<DietPlanDto>> AddDietPlanItemAsync(Guid farmId, Guid dietPlanId, AddDietPlanItemRequest request);
    Task<Result> RemoveDietPlanItemAsync(Guid farmId, Guid dietPlanId, Guid itemId);

    // Feeding schedules
    Task<Result<List<FeedingScheduleDto>>> GetSchedulesAsync(Guid farmId, Guid? dietPlanId);
    Task<Result<FeedingScheduleDto>> CreateScheduleAsync(Guid farmId, CreateFeedingScheduleRequest request);
    Task<Result<FeedingScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateFeedingScheduleRequest request);
    Task<Result> DeleteScheduleAsync(Guid farmId, Guid id);

    // Feeding tasks
    Task<Result<List<FeedingTaskDto>>> GenerateTasksAsync(Guid farmId, GenerateFeedingTasksRequest request);
    Task<Result<PagedResult<FeedingTaskDto>>> GetTasksAsync(Guid farmId, FeedingTaskListFilter filter);
    Task<Result<FeedingTaskDto>> CompleteTaskAsync(Guid farmId, Guid id, CompleteFeedingTaskRequest request);
    Task<Result<FeedingTaskDto>> SkipTaskAsync(Guid farmId, Guid id, CompleteFeedingTaskRequest request);

    // Reports
    Task<Result<List<ConsumptionTrendPointDto>>> GetConsumptionTrendAsync(Guid farmId, string period, DateTime? from, DateTime? to);
    Task<Result<List<FeedTypeBreakdownDto>>> GetConsumptionByFeedTypeAsync(Guid farmId, DateTime? from, DateTime? to);
    Task<Result<List<AnimalConsumptionDto>>> GetConsumptionByAnimalAsync(Guid farmId, DateTime? from, DateTime? to);
    Task<Result<List<LocationConsumptionDto>>> GetConsumptionByLocationAsync(Guid farmId, DateTime? from, DateTime? to);
    Task<Result<FeedCostSummaryDto>> GetCostSummaryAsync(Guid farmId, DateTime? from, DateTime? to);
}
