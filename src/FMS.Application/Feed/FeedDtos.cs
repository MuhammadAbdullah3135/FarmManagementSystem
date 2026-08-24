using FMS.Domain.Enums;

namespace FMS.Application.Feed;

public class FeedTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public FeedCategory Category { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public FeedUnit Unit { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal CostPerUnit { get; set; }
    public string? Notes { get; set; }
}

public class CreateFeedTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public FeedCategory Category { get; set; }
    public FeedUnit Unit { get; set; }
    public decimal CostPerUnit { get; set; }
    public string? Notes { get; set; }
}

public class UpdateFeedTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public FeedCategory Category { get; set; }
    public FeedUnit Unit { get; set; }
    public decimal CostPerUnit { get; set; }
    public string? Notes { get; set; }
}

public class FeedStockDto
{
    public Guid FeedTypeId { get; set; }
    public string FeedTypeName { get; set; } = string.Empty;
    public FeedCategory Category { get; set; }
    public FeedUnit Unit { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal QuantityPurchased { get; set; }
    public decimal QuantityConsumed { get; set; }
    public decimal NetAdjustments { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal TotalCost { get; set; }
}

public class RecordStockMovementRequest
{
    public Guid FeedTypeId { get; set; }
    public StockMovementType MovementType { get; set; }

    /// <summary>
    /// Positive magnitude for Purchase/Consumption. For Adjustment, may be
    /// negative (stock correction down) or positive (correction up).
    /// </summary>
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Supplier { get; set; }
    public string? Notes { get; set; }
    public DateTime? MovementDate { get; set; }
}

public class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid FeedTypeId { get; set; }
    public string FeedTypeName { get; set; } = string.Empty;
    public StockMovementType MovementType { get; set; }
    public string MovementTypeName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SignedQuantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public string? Supplier { get; set; }
    public string? Notes { get; set; }
    public DateTime MovementDate { get; set; }
}

public class CreateFeedRecordRequest
{
    public Guid FeedTypeId { get; set; }

    /// <summary>Individual feeding target. Exactly one of AnimalId / LocationId is required.</summary>
    public Guid? AnimalId { get; set; }

    /// <summary>Group feeding target (all animals in a location). Exactly one of AnimalId / LocationId is required.</summary>
    public Guid? LocationId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? FedAt { get; set; }
    public string? Notes { get; set; }
}

public class UpdateFeedRecordRequest
{
    public decimal Quantity { get; set; }
    public DateTime? FedAt { get; set; }
    public string? Notes { get; set; }
}

public class FeedRecordListFilter
{
    public Guid? AnimalId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? FeedTypeId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class FeedRecordDto
{
    public Guid Id { get; set; }
    public Guid FeedTypeId { get; set; }
    public string FeedTypeName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public Guid? AnimalId { get; set; }
    public string? AnimalTagNumber { get; set; }
    public string? AnimalName { get; set; }
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime FedAt { get; set; }
    public string? Notes { get; set; }
}

public class DietPlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? AnimalTypeId { get; set; }
    public string? AnimalTypeName { get; set; }
    public Guid? BreedId { get; set; }
    public string? BreedName { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public string? AgeCategoryName { get; set; }
    public decimal? MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public List<DietPlanItemDto> Items { get; set; } = new();
    public int ScheduleCount { get; set; }
}

public class DietPlanItemDto
{
    public Guid Id { get; set; }
    public Guid FeedTypeId { get; set; }
    public string FeedTypeName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal QuantityPerFeeding { get; set; }
}

public class CreateDietPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public decimal? MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public string? Notes { get; set; }
    public List<AddDietPlanItemRequest>? Items { get; set; }
}

public class UpdateDietPlanRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? AnimalTypeId { get; set; }
    public Guid? BreedId { get; set; }
    public Guid? AgeCategoryId { get; set; }
    public decimal? MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public class AddDietPlanItemRequest
{
    public Guid FeedTypeId { get; set; }
    public decimal QuantityPerFeeding { get; set; }
}

public class FeedingScheduleDto
{
    public Guid Id { get; set; }
    public Guid DietPlanId { get; set; }
    public string DietPlanName { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool IsActive { get; set; }
}

public class CreateFeedingScheduleRequest
{
    public Guid DietPlanId { get; set; }

    /// <summary>Time of day in HH:mm format.</summary>
    public string TimeOfDay { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public class UpdateFeedingScheduleRequest
{
    /// <summary>Time of day in HH:mm format. Null to keep current.</summary>
    public string? TimeOfDay { get; set; }
    public string? Label { get; set; }
    public bool? IsActive { get; set; }
}

public class FeedingTaskDto
{
    public Guid Id { get; set; }
    public Guid FeedingScheduleId { get; set; }
    public Guid DietPlanId { get; set; }
    public string DietPlanName { get; set; } = string.Empty;
    public DateTime TaskDate { get; set; }
    public string TimeOfDay { get; set; } = string.Empty;
    public FeedingTaskStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int TargetAnimalCount { get; set; }
    public List<DietPlanItemDto> Items { get; set; } = new();
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
}

public class GenerateFeedingTasksRequest
{
    public DateTime Date { get; set; }
}

public class FeedingTaskListFilter
{
    public DateTime? Date { get; set; }
    public Guid? DietPlanId { get; set; }

    /// <summary>Pending / Completed / Skipped.</summary>
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class CompleteFeedingTaskRequest
{
    public string? Notes { get; set; }
}

public class ConsumptionTrendPointDto
{
    public DateTime PeriodStart { get; set; }

    /// <summary>Chart label: yyyy-MM-dd for day/week, yyyy-MM for month.</summary>
    public string Label { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Cost { get; set; }
}

public class FeedTypeBreakdownDto
{
    public Guid FeedTypeId { get; set; }
    public string FeedTypeName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Cost { get; set; }

    /// <summary>Share of total consumption cost in the period, 0-100.</summary>
    public decimal SharePercent { get; set; }
}

public class AnimalConsumptionDto
{
    public Guid AnimalId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal Quantity { get; set; }
    public decimal Cost { get; set; }
}

public class LocationConsumptionDto
{
    public Guid LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Cost { get; set; }
}

public class FeedCostSummaryDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal TotalConsumedQuantity { get; set; }
    public decimal TotalConsumedCost { get; set; }
    public decimal TotalPurchasedQuantity { get; set; }
    public decimal TotalPurchasedCost { get; set; }

    /// <summary>Current stock valued at each feed type's current cost per unit.</summary>
    public decimal CurrentInventoryValue { get; set; }
}
