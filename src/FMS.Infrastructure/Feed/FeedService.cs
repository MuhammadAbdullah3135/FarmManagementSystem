using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace FMS.Infrastructure.Feed;

public class FeedService : IFeedService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public FeedService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // Feed types

    public async Task<Result<List<FeedTypeDto>>> GetFeedTypesAsync(Guid farmId)
    {
        var types = await _context.FeedTypes
            .Where(ft => ft.FarmId == farmId)
            .OrderBy(ft => ft.Name)
            .ToListAsync();

        return Result<List<FeedTypeDto>>.Success(types.Select(MapFeedType).ToList());
    }

    public async Task<Result<FeedTypeDto>> GetFeedTypeByIdAsync(Guid farmId, Guid id)
    {
        var feedType = await _context.FeedTypes
            .FirstOrDefaultAsync(ft => ft.Id == id && ft.FarmId == farmId);

        if (feedType == null)
            return Result<FeedTypeDto>.NotFound("Feed type not found");

        return Result<FeedTypeDto>.Success(MapFeedType(feedType));
    }

    public async Task<Result<FeedTypeDto>> CreateFeedTypeAsync(Guid farmId, CreateFeedTypeRequest request)
    {
        var validationError = ValidateDetails(request.Name, request.CostPerUnit);
        if (validationError != null)
            return Result<FeedTypeDto>.Validation(validationError);

        var duplicateExists = await _context.FeedTypes
            .AnyAsync(ft => ft.FarmId == farmId && ft.Name.ToLower() == request.Name.ToLower());
        if (duplicateExists)
            return Result<FeedTypeDto>.Conflict($"A feed type named '{request.Name}' already exists");

        var feedType = new FeedType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            Category = request.Category,
            Unit = request.Unit,
            CostPerUnit = request.CostPerUnit,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.FeedTypes.Add(feedType);
        await _context.SaveChangesAsync();

        return Result<FeedTypeDto>.Success(MapFeedType(feedType));
    }

    public async Task<Result<FeedTypeDto>> UpdateFeedTypeAsync(Guid farmId, Guid id, UpdateFeedTypeRequest request)
    {
        var feedType = await _context.FeedTypes
            .FirstOrDefaultAsync(ft => ft.Id == id && ft.FarmId == farmId);
        if (feedType == null)
            return Result<FeedTypeDto>.NotFound("Feed type not found");

        var validationError = ValidateDetails(request.Name, request.CostPerUnit);
        if (validationError != null)
            return Result<FeedTypeDto>.Validation(validationError);

        var duplicateExists = await _context.FeedTypes
            .AnyAsync(ft => ft.FarmId == farmId && ft.Id != id && ft.Name.ToLower() == request.Name.ToLower());
        if (duplicateExists)
            return Result<FeedTypeDto>.Conflict($"A feed type named '{request.Name}' already exists");

        feedType.Name = request.Name.Trim();
        feedType.Category = request.Category;
        feedType.Unit = request.Unit;
        feedType.CostPerUnit = request.CostPerUnit;
        feedType.Notes = request.Notes;
        feedType.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Result<FeedTypeDto>.Success(MapFeedType(feedType));
    }

    public async Task<Result> DeleteFeedTypeAsync(Guid farmId, Guid id)
    {
        var feedType = await _context.FeedTypes
            .FirstOrDefaultAsync(ft => ft.Id == id && ft.FarmId == farmId);
        if (feedType == null)
            return Result.NotFound("Feed type not found");

        var hasMovements = await _context.FeedStockMovements
            .AnyAsync(m => m.FeedTypeId == id);
        if (hasMovements)
            return Result.Conflict("Cannot delete a feed type that has stock movements");

        var hasRecords = await _context.FeedRecords
            .AnyAsync(r => r.FeedTypeId == id);
        if (hasRecords)
            return Result.Conflict("Cannot delete a feed type that has feed records");

        var hasPlanItems = await _context.DietPlanItems
            .AnyAsync(i => i.FeedTypeId == id);
        if (hasPlanItems)
            return Result.Conflict("Cannot delete a feed type that is used in a diet plan");

        _context.FeedTypes.Remove(feedType);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Inventory

    public async Task<Result<StockMovementDto>> RecordStockMovementAsync(Guid farmId, RecordStockMovementRequest request)
    {
        var feedType = await _context.FeedTypes
            .FirstOrDefaultAsync(ft => ft.Id == request.FeedTypeId && ft.FarmId == farmId);
        if (feedType == null)
            return Result<StockMovementDto>.NotFound("Feed type not found");

        if (request.MovementType == StockMovementType.Adjustment)
        {
            if (request.Quantity == 0)
                return Result<StockMovementDto>.Validation("Adjustment quantity cannot be zero");
        }
        else if (request.Quantity <= 0)
        {
            return Result<StockMovementDto>.Validation("Quantity must be greater than zero");
        }

        if (request.UnitCost.HasValue && request.UnitCost.Value < 0)
            return Result<StockMovementDto>.Validation("Unit cost cannot be negative");

        var movementDate = request.MovementDate ?? DateTime.UtcNow;
        if (movementDate > DateTime.UtcNow.AddMinutes(5))
            return Result<StockMovementDto>.Validation("Movement date cannot be in the future");

        var currentStock = await ComputeStockAsync(farmId, feedType.Id);
        var outgoingAmount = request.MovementType switch
        {
            StockMovementType.Consumption => request.Quantity,
            StockMovementType.Adjustment when request.Quantity < 0 => -request.Quantity,
            _ => 0m
        };

        if (currentStock < outgoingAmount)
            return Result<StockMovementDto>.Conflict(
                $"Insufficient stock: current stock is {currentStock} {feedType.Unit}, attempted to remove {outgoingAmount}");

        var movement = new FeedStockMovement
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FeedTypeId = feedType.Id,
            MovementType = request.MovementType,
            Quantity = request.Quantity,
            UnitCost = request.UnitCost,
            TotalCost = request.UnitCost.HasValue ? Math.Abs(request.Quantity) * request.UnitCost.Value : null,
            Supplier = request.Supplier,
            Notes = request.Notes,
            MovementDate = movementDate,
            CreatedAt = DateTime.UtcNow
        };

        _context.FeedStockMovements.Add(movement);
        await _context.SaveChangesAsync();

        return Result<StockMovementDto>.Success(MapMovement(movement, feedType));
    }

    public async Task<Result<PagedResult<StockMovementDto>>> GetStockMovementsAsync(Guid farmId, Guid? feedTypeId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _context.FeedStockMovements
            .Include(m => m.FeedType)
            .Where(m => m.FarmId == farmId);

        if (feedTypeId.HasValue)
            query = query.Where(m => m.FeedTypeId == feedTypeId.Value);

        var totalCount = await query.CountAsync();

        var movements = await query
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<StockMovementDto>>.Success(new PagedResult<StockMovementDto>
        {
            Items = movements.Select(m => MapMovement(m, m.FeedType)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<List<FeedStockDto>>> GetStockAsync(Guid farmId)
    {
        var types = await _context.FeedTypes
            .Where(ft => ft.FarmId == farmId)
            .OrderBy(ft => ft.Name)
            .ToListAsync();

        var movements = await _context.FeedStockMovements
            .Where(m => m.FarmId == farmId)
            .ToListAsync();

        var stock = types.Select(t =>
        {
            var typeMovements = movements.Where(m => m.FeedTypeId == t.Id).ToList();
            var purchased = typeMovements
                .Where(m => m.MovementType == StockMovementType.Purchase)
                .Sum(m => m.Quantity);
            var consumed = typeMovements
                .Where(m => m.MovementType == StockMovementType.Consumption)
                .Sum(m => m.Quantity);
            var adjusted = typeMovements
                .Where(m => m.MovementType == StockMovementType.Adjustment)
                .Sum(m => m.Quantity);

            return new FeedStockDto
            {
                FeedTypeId = t.Id,
                FeedTypeName = t.Name,
                Category = t.Category,
                Unit = t.Unit,
                UnitName = t.Unit.ToString(),
                QuantityPurchased = purchased,
                QuantityConsumed = consumed,
                NetAdjustments = adjusted,
                CurrentStock = purchased - consumed + adjusted,
                TotalCost = typeMovements.Sum(m => m.TotalCost ?? 0)
            };
        }).ToList();

        return Result<List<FeedStockDto>>.Success(stock);
    }

    public async Task<Result<FeedStockDto>> GetStockByFeedTypeAsync(Guid farmId, Guid feedTypeId)
    {
        var allStock = await GetStockAsync(farmId);
        if (!allStock.IsSuccess)
            return Result<FeedStockDto>.Failure(allStock.Error!);

        var stock = allStock.Value!.FirstOrDefault(s => s.FeedTypeId == feedTypeId);
        if (stock == null)
            return Result<FeedStockDto>.NotFound("Feed type not found");

        return Result<FeedStockDto>.Success(stock);
    }

    // Feed records

    public async Task<Result<FeedRecordDto>> CreateFeedRecordAsync(Guid farmId, CreateFeedRecordRequest request)
    {
        var feedType = await _context.FeedTypes
            .FirstOrDefaultAsync(ft => ft.Id == request.FeedTypeId && ft.FarmId == farmId);
        if (feedType == null)
            return Result<FeedRecordDto>.NotFound("Feed type not found");

        if (request.Quantity <= 0)
            return Result<FeedRecordDto>.Validation("Quantity must be greater than zero");

        if (request.AnimalId.HasValue && request.LocationId.HasValue)
            return Result<FeedRecordDto>.Validation("Specify either an animal or a location, not both");
        if (!request.AnimalId.HasValue && !request.LocationId.HasValue)
            return Result<FeedRecordDto>.Validation("Either AnimalId or LocationId must be specified");

        Animal? animal = null;
        if (request.AnimalId.HasValue)
        {
            animal = await _context.Animals
                .FirstOrDefaultAsync(a => a.Id == request.AnimalId.Value && a.FarmId == farmId && !a.IsDeleted);
            if (animal == null)
                return Result<FeedRecordDto>.NotFound("Animal not found");
        }

        Location? location = null;
        if (request.LocationId.HasValue)
        {
            location = await _context.Locations
                .FirstOrDefaultAsync(l => l.Id == request.LocationId.Value && l.FarmId == farmId);
            if (location == null)
                return Result<FeedRecordDto>.NotFound("Location not found");
        }

        var fedAt = request.FedAt ?? DateTime.UtcNow;
        if (fedAt > DateTime.UtcNow.AddMinutes(5))
            return Result<FeedRecordDto>.Validation("Fed date cannot be in the future");

        var currentStock = await ComputeStockAsync(farmId, feedType.Id);
        if (currentStock < request.Quantity)
            return Result<FeedRecordDto>.Conflict(
                $"Insufficient stock: current stock is {currentStock} {UnitAbbreviation(feedType.Unit)}, attempted to feed {request.Quantity}");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var movement = new FeedStockMovement
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FeedTypeId = feedType.Id,
            MovementType = StockMovementType.Consumption,
            Quantity = Math.Round(request.Quantity, 2),
            MovementDate = fedAt,
            Notes = "Auto-deducted for feed record",
            CreatedAt = now,
            CreatedBy = userId
        };
        _context.FeedStockMovements.Add(movement);

        var record = new FeedRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FeedTypeId = feedType.Id,
            AnimalId = animal?.Id,
            LocationId = location?.Id,
            Quantity = Math.Round(request.Quantity, 2),
            UnitCost = feedType.CostPerUnit,
            FedAt = fedAt,
            Notes = request.Notes,
            StockMovementId = movement.Id,
            CreatedAt = now,
            CreatedBy = userId
        };
        _context.FeedRecords.Add(record);

        if (animal != null)
        {
            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = animal.Id,
                FarmId = farmId,
                EventType = TimelineEventTypes.Fed,
                Title = $"Fed {record.Quantity} {UnitAbbreviation(feedType.Unit)} {feedType.Name}",
                Description = request.Notes,
                RelatedEntityId = record.Id,
                RelatedEntityType = "FeedRecord",
                OccurredAt = fedAt,
                CreatedAt = now,
                CreatedBy = userId
            });
        }

        await _context.SaveChangesAsync();

        return Result<FeedRecordDto>.Success(MapFeedRecord(record));
    }

    public async Task<Result<FeedRecordDto>> GetFeedRecordByIdAsync(Guid farmId, Guid id)
    {
        var record = await _context.FeedRecords
            .Include(r => r.FeedType)
            .Include(r => r.Animal)
            .Include(r => r.Location)
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (record == null)
            return Result<FeedRecordDto>.NotFound("Feed record not found");

        return Result<FeedRecordDto>.Success(MapFeedRecord(record));
    }

    public async Task<Result<PagedResult<FeedRecordDto>>> GetFeedRecordsAsync(Guid farmId, FeedRecordListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = _context.FeedRecords
            .Include(r => r.FeedType)
            .Include(r => r.Animal)
            .Include(r => r.Location)
            .Where(r => r.FarmId == farmId);

        if (filter.AnimalId.HasValue)
            query = query.Where(r => r.AnimalId == filter.AnimalId.Value);
        if (filter.LocationId.HasValue)
            query = query.Where(r => r.LocationId == filter.LocationId.Value);
        if (filter.FeedTypeId.HasValue)
            query = query.Where(r => r.FeedTypeId == filter.FeedTypeId.Value);
        if (filter.From.HasValue)
            query = query.Where(r => r.FedAt >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(r => r.FedAt <= filter.To.Value);

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderByDescending(r => r.FedAt)
            .ThenByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<FeedRecordDto>>.Success(new PagedResult<FeedRecordDto>
        {
            Items = records.Select(MapFeedRecord).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<FeedRecordDto>> UpdateFeedRecordAsync(Guid farmId, Guid id, UpdateFeedRecordRequest request)
    {
        var record = await _context.FeedRecords
            .Include(r => r.FeedType)
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (record == null)
            return Result<FeedRecordDto>.NotFound("Feed record not found");

        if (request.Quantity <= 0)
            return Result<FeedRecordDto>.Validation("Quantity must be greater than zero");

        var fedAt = request.FedAt ?? record.FedAt;
        if (fedAt > DateTime.UtcNow.AddMinutes(5))
            return Result<FeedRecordDto>.Validation("Fed date cannot be in the future");

        var oldQuantity = record.Quantity;
        var newQuantity = Math.Round(request.Quantity, 2);

        if (newQuantity > oldQuantity)
        {
            var currentStock = await ComputeStockAsync(farmId, record.FeedTypeId);
            var extraNeeded = newQuantity - oldQuantity;
            if (currentStock < extraNeeded)
                return Result<FeedRecordDto>.Conflict(
                    $"Insufficient stock: current stock is {currentStock} {UnitAbbreviation(record.FeedType.Unit)}, additional {extraNeeded} required");
        }

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        record.Quantity = newQuantity;
        record.FedAt = fedAt;
        record.Notes = request.Notes;
        record.ModifiedAt = now;
        record.ModifiedBy = userId;

        if (record.StockMovementId.HasValue)
        {
            var movement = await _context.FeedStockMovements
                .FirstOrDefaultAsync(m => m.Id == record.StockMovementId.Value);
            if (movement != null)
            {
                movement.Quantity = newQuantity;
                movement.TotalCost = newQuantity * movement.UnitCost;
                movement.MovementDate = fedAt;
                movement.ModifiedAt = now;
                movement.ModifiedBy = userId;
            }
        }

        if (record.AnimalId.HasValue)
        {
            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = record.AnimalId.Value,
                FarmId = farmId,
                EventType = TimelineEventTypes.FeedUpdated,
                Title = $"Feed updated: {oldQuantity} → {newQuantity} {UnitAbbreviation(record.FeedType.Unit)} {record.FeedType.Name}",
                RelatedEntityId = record.Id,
                RelatedEntityType = "FeedRecord",
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = userId
            });
        }

        await _context.SaveChangesAsync();

        return Result<FeedRecordDto>.Success(MapFeedRecord(record));
    }

    public async Task<Result> DeleteFeedRecordAsync(Guid farmId, Guid id)
    {
        var record = await _context.FeedRecords
            .Include(r => r.FeedType)
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (record == null)
            return Result.NotFound("Feed record not found");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        if (record.StockMovementId.HasValue)
        {
            var movement = await _context.FeedStockMovements
                .FirstOrDefaultAsync(m => m.Id == record.StockMovementId.Value);
            if (movement != null)
                _context.FeedStockMovements.Remove(movement);
        }

        _context.FeedRecords.Remove(record);

        if (record.AnimalId.HasValue)
        {
            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = record.AnimalId.Value,
                FarmId = farmId,
                EventType = TimelineEventTypes.FeedDeleted,
                Title = $"Feed entry deleted: {record.Quantity} {UnitAbbreviation(record.FeedType.Unit)} {record.FeedType.Name} ({record.FedAt:yyyy-MM-dd})",
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = userId
            });
        }

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Diet plans

    public async Task<Result<List<DietPlanDto>>> GetDietPlansAsync(Guid farmId)
    {
        var plans = await _context.DietPlans
            .Include(p => p.AnimalType)
            .Include(p => p.Breed)
            .Include(p => p.AgeCategory)
            .Include(p => p.Items).ThenInclude(i => i.FeedType)
            .Include(p => p.Schedules)
            .Where(p => p.FarmId == farmId)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Result<List<DietPlanDto>>.Success(plans.Select(MapDietPlan).ToList());
    }

    public async Task<Result<DietPlanDto>> GetDietPlanByIdAsync(Guid farmId, Guid id)
    {
        var plan = await _context.DietPlans
            .Include(p => p.AnimalType)
            .Include(p => p.Breed)
            .Include(p => p.AgeCategory)
            .Include(p => p.Items).ThenInclude(i => i.FeedType)
            .Include(p => p.Schedules)
            .FirstOrDefaultAsync(p => p.Id == id && p.FarmId == farmId);
        if (plan == null)
            return Result<DietPlanDto>.NotFound("Diet plan not found");

        return Result<DietPlanDto>.Success(MapDietPlan(plan));
    }

    public async Task<Result<DietPlanDto>> CreateDietPlanAsync(Guid farmId, CreateDietPlanRequest request)
    {
        var validationError = ValidateDietPlanDetails(request.Name, request.MinWeightKg, request.MaxWeightKg);
        if (validationError != null)
            return Result<DietPlanDto>.Validation(validationError);

        var criteriaError = await ValidateDietPlanCriteriaAsync(farmId, request.AnimalTypeId, request.BreedId, request.AgeCategoryId);
        if (criteriaError != null)
            return Result<DietPlanDto>.Failure(criteriaError);

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var plan = new DietPlan
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name.Trim(),
            AnimalTypeId = request.AnimalTypeId,
            BreedId = request.BreedId,
            AgeCategoryId = request.AgeCategoryId,
            MinWeightKg = request.MinWeightKg,
            MaxWeightKg = request.MaxWeightKg,
            IsActive = true,
            Notes = request.Notes,
            CreatedAt = now,
            CreatedBy = userId
        };

        if (request.Items != null && request.Items.Count > 0)
        {
            var itemError = await AddItemsToPlanInternalAsync(farmId, plan, request.Items, userId, now);
            if (itemError != null)
                return Result<DietPlanDto>.Failure(itemError);
        }

        _context.DietPlans.Add(plan);
        await _context.SaveChangesAsync();

        return await GetDietPlanByIdAsync(farmId, plan.Id);
    }

    public async Task<Result<DietPlanDto>> UpdateDietPlanAsync(Guid farmId, Guid id, UpdateDietPlanRequest request)
    {
        var plan = await _context.DietPlans
            .FirstOrDefaultAsync(p => p.Id == id && p.FarmId == farmId);
        if (plan == null)
            return Result<DietPlanDto>.NotFound("Diet plan not found");

        var validationError = ValidateDietPlanDetails(request.Name, request.MinWeightKg, request.MaxWeightKg);
        if (validationError != null)
            return Result<DietPlanDto>.Validation(validationError);

        var criteriaError = await ValidateDietPlanCriteriaAsync(farmId, request.AnimalTypeId, request.BreedId, request.AgeCategoryId);
        if (criteriaError != null)
            return Result<DietPlanDto>.Failure(criteriaError);

        plan.Name = request.Name.Trim();
        plan.AnimalTypeId = request.AnimalTypeId;
        plan.BreedId = request.BreedId;
        plan.AgeCategoryId = request.AgeCategoryId;
        plan.MinWeightKg = request.MinWeightKg;
        plan.MaxWeightKg = request.MaxWeightKg;
        plan.IsActive = request.IsActive;
        plan.Notes = request.Notes;
        plan.ModifiedAt = DateTime.UtcNow;
        plan.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await GetDietPlanByIdAsync(farmId, id);
    }

    public async Task<Result> DeleteDietPlanAsync(Guid farmId, Guid id)
    {
        var plan = await _context.DietPlans
            .FirstOrDefaultAsync(p => p.Id == id && p.FarmId == farmId);
        if (plan == null)
            return Result.NotFound("Diet plan not found");

        var hasTasks = await _context.FeedingTasks
            .AnyAsync(t => t.DietPlanId == id);
        if (hasTasks)
            return Result.Conflict("Cannot delete a diet plan that has generated feeding tasks; deactivate it instead");

        var items = await _context.DietPlanItems.Where(i => i.DietPlanId == id).ToListAsync();
        var schedules = await _context.FeedingSchedules.Where(s => s.DietPlanId == id).ToListAsync();
        _context.DietPlanItems.RemoveRange(items);
        _context.FeedingSchedules.RemoveRange(schedules);
        _context.DietPlans.Remove(plan);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<DietPlanDto>> AddDietPlanItemAsync(Guid farmId, Guid dietPlanId, AddDietPlanItemRequest request)
    {
        var plan = await _context.DietPlans
            .FirstOrDefaultAsync(p => p.Id == dietPlanId && p.FarmId == farmId);
        if (plan == null)
            return Result<DietPlanDto>.NotFound("Diet plan not found");

        var error = await AddItemsToPlanInternalAsync(farmId, plan,
            new List<AddDietPlanItemRequest> { request }, _currentUser.GetUserId(), DateTime.UtcNow);
        if (error != null)
            return Result<DietPlanDto>.Failure(error);

        await _context.SaveChangesAsync();

        return await GetDietPlanByIdAsync(farmId, dietPlanId);
    }

    public async Task<Result> RemoveDietPlanItemAsync(Guid farmId, Guid dietPlanId, Guid itemId)
    {
        var item = await _context.DietPlanItems
            .Include(i => i.DietPlan)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.DietPlanId == dietPlanId && i.DietPlan.FarmId == farmId);
        if (item == null)
            return Result.NotFound("Diet plan item not found");

        _context.DietPlanItems.Remove(item);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Feeding schedules

    public async Task<Result<List<FeedingScheduleDto>>> GetSchedulesAsync(Guid farmId, Guid? dietPlanId)
    {
        var query = _context.FeedingSchedules
            .Include(s => s.DietPlan)
            .Where(s => s.FarmId == farmId);

        if (dietPlanId.HasValue)
            query = query.Where(s => s.DietPlanId == dietPlanId.Value);

        var schedules = await query
            .OrderBy(s => s.TimeOfDay)
            .ToListAsync();

        return Result<List<FeedingScheduleDto>>.Success(schedules.Select(MapSchedule).ToList());
    }

    public async Task<Result<FeedingScheduleDto>> CreateScheduleAsync(Guid farmId, CreateFeedingScheduleRequest request)
    {
        var plan = await _context.DietPlans
            .FirstOrDefaultAsync(p => p.Id == request.DietPlanId && p.FarmId == farmId);
        if (plan == null)
            return Result<FeedingScheduleDto>.NotFound("Diet plan not found");

        if (!TryParseTimeOfDay(request.TimeOfDay, out var timeOfDay))
            return Result<FeedingScheduleDto>.Validation("TimeOfDay must be in HH:mm format (e.g. 07:30)");

        var duplicate = await _context.FeedingSchedules
            .AnyAsync(s => s.DietPlanId == request.DietPlanId && s.TimeOfDay == timeOfDay);
        if (duplicate)
            return Result<FeedingScheduleDto>.Conflict($"A schedule at {FormatTime(timeOfDay)} already exists for this diet plan");

        if (request.Label?.Length > 50)
            return Result<FeedingScheduleDto>.Validation("Label cannot exceed 50 characters");

        var schedule = new FeedingSchedule
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            DietPlanId = request.DietPlanId,
            TimeOfDay = timeOfDay,
            Label = request.Label,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.FeedingSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        return Result<FeedingScheduleDto>.Success(MapSchedule(schedule));
    }

    public async Task<Result<FeedingScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateFeedingScheduleRequest request)
    {
        var schedule = await _context.FeedingSchedules
            .Include(s => s.DietPlan)
            .FirstOrDefaultAsync(s => s.Id == id && s.FarmId == farmId);
        if (schedule == null)
            return Result<FeedingScheduleDto>.NotFound("Feeding schedule not found");

        var timeOfDay = schedule.TimeOfDay;
        if (request.TimeOfDay != null)
        {
            if (!TryParseTimeOfDay(request.TimeOfDay, out var parsed))
                return Result<FeedingScheduleDto>.Validation("TimeOfDay must be in HH:mm format (e.g. 07:30)");
            timeOfDay = parsed;
        }

        if (timeOfDay != schedule.TimeOfDay)
        {
            var duplicate = await _context.FeedingSchedules
                .AnyAsync(s => s.DietPlanId == schedule.DietPlanId && s.Id != id && s.TimeOfDay == timeOfDay);
            if (duplicate)
                return Result<FeedingScheduleDto>.Conflict($"A schedule at {FormatTime(timeOfDay)} already exists for this diet plan");
            schedule.TimeOfDay = timeOfDay;
        }

        if (request.Label != null)
        {
            if (request.Label.Length > 50)
                return Result<FeedingScheduleDto>.Validation("Label cannot exceed 50 characters");
            schedule.Label = request.Label;
        }

        if (request.IsActive.HasValue)
            schedule.IsActive = request.IsActive.Value;

        schedule.ModifiedAt = DateTime.UtcNow;
        schedule.ModifiedBy = _currentUser.GetUserId();
        await _context.SaveChangesAsync();

        return Result<FeedingScheduleDto>.Success(MapSchedule(schedule));
    }

    public async Task<Result> DeleteScheduleAsync(Guid farmId, Guid id)
    {
        var schedule = await _context.FeedingSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.FarmId == farmId);
        if (schedule == null)
            return Result.NotFound("Feeding schedule not found");

        var hasTasks = await _context.FeedingTasks
            .AnyAsync(t => t.FeedingScheduleId == id);
        if (hasTasks)
            return Result.Conflict("Cannot delete a schedule that has generated tasks; deactivate it instead");

        _context.FeedingSchedules.Remove(schedule);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Feeding tasks

    public async Task<Result<List<FeedingTaskDto>>> GenerateTasksAsync(Guid farmId, GenerateFeedingTasksRequest request)
    {
        var date = request.Date.Date;
        if (date < DateTime.UtcNow.Date)
            return Result<List<FeedingTaskDto>>.Validation("Cannot generate feeding tasks for a past date");

        var schedules = await _context.FeedingSchedules
            .Include(s => s.DietPlan)
            .Where(s => s.FarmId == farmId && s.IsActive && s.DietPlan.IsActive)
            .ToListAsync();

        if (schedules.Count == 0)
            return Result<List<FeedingTaskDto>>.Success(new List<FeedingTaskDto>());

        var existingScheduleIds = (await _context.FeedingTasks
                .Where(t => t.FarmId == farmId && t.TaskDate == date)
                .Select(t => t.FeedingScheduleId)
                .ToListAsync())
            .ToHashSet();

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;
        var created = new List<FeedingTask>();

        var schedulesToCreate = schedules.Where(s => !existingScheduleIds.Contains(s.Id)).ToList();

        // One pass for every plan in play, rather than the per-schedule count this used to do.
        // Two schedules can share a plan, and every plan's candidates come from the same rows,
        // so asking per schedule repeated the same work — and each answer cost a query, plus
        // another for the animals' weights whenever the plan had a weight range.
        var targetCountsByPlan = await CountMatchingAnimalsByPlanAsync(
            farmId, schedulesToCreate.Select(s => s.DietPlan).DistinctBy(plan => plan.Id).ToList());

        foreach (var schedule in schedulesToCreate)
        {
            var task = new FeedingTask
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                FeedingScheduleId = schedule.Id,
                DietPlanId = schedule.DietPlanId,
                TaskDate = date,
                TimeOfDay = schedule.TimeOfDay,
                Status = FeedingTaskStatus.Pending,
                TargetAnimalCount = targetCountsByPlan[schedule.DietPlanId],
                CreatedAt = now,
                CreatedBy = userId
            };
            _context.FeedingTasks.Add(task);
            created.Add(task);
        }

        if (created.Count > 0)
            await _context.SaveChangesAsync();

        var itemsByPlan = await LoadPlanItemsAsync(created.Select(t => t.DietPlanId).Distinct().ToList());
        return Result<List<FeedingTaskDto>>.Success(created.Select(t => MapTask(t, itemsByPlan)).ToList());
    }

    public async Task<Result<PagedResult<FeedingTaskDto>>> GetTasksAsync(Guid farmId, FeedingTaskListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        FeedingTaskStatus? status = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<FeedingTaskStatus>(filter.Status, ignoreCase: true, out var parsed))
                return Result<PagedResult<FeedingTaskDto>>.Validation(
                    "Status must be one of: Pending, Completed, Skipped");
            status = parsed;
        }

        var query = _context.FeedingTasks
            .Include(t => t.DietPlan)
            .Where(t => t.FarmId == farmId);

        if (filter.Date.HasValue)
            query = query.Where(t => t.TaskDate == filter.Date.Value.Date);
        if (filter.DietPlanId.HasValue)
            query = query.Where(t => t.DietPlanId == filter.DietPlanId.Value);
        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        var totalCount = await query.CountAsync();

        var tasks = await query
            .OrderBy(t => t.TaskDate)
            .ThenBy(t => t.TimeOfDay)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var itemsByPlan = await LoadPlanItemsAsync(tasks.Select(t => t.DietPlanId).Distinct().ToList());

        return Result<PagedResult<FeedingTaskDto>>.Success(new PagedResult<FeedingTaskDto>
        {
            Items = tasks.Select(t => MapTask(t, itemsByPlan)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<FeedingTaskDto>> CompleteTaskAsync(Guid farmId, Guid id, CompleteFeedingTaskRequest request)
        => await CloseTaskAsync(farmId, id, FeedingTaskStatus.Completed, request);

    public async Task<Result<FeedingTaskDto>> SkipTaskAsync(Guid farmId, Guid id, CompleteFeedingTaskRequest request)
        => await CloseTaskAsync(farmId, id, FeedingTaskStatus.Skipped, request);

    private async Task<Result<FeedingTaskDto>> CloseTaskAsync(Guid farmId, Guid id, FeedingTaskStatus targetStatus, CompleteFeedingTaskRequest request)
    {
        var task = await _context.FeedingTasks
            .Include(t => t.DietPlan)
            .FirstOrDefaultAsync(t => t.Id == id && t.FarmId == farmId);
        if (task == null)
            return Result<FeedingTaskDto>.NotFound("Feeding task not found");

        if (task.Status != FeedingTaskStatus.Pending)
            return Result<FeedingTaskDto>.Conflict($"Task is already {task.Status}");

        var now = DateTime.UtcNow;
        task.Status = targetStatus;
        task.CompletedAt = now;
        task.CompletedBy = _currentUser.GetUserId();
        task.Notes = request.Notes;
        task.ModifiedAt = now;
        task.ModifiedBy = task.CompletedBy;

        await _context.SaveChangesAsync();

        var itemsByPlan = await LoadPlanItemsAsync(new List<Guid> { task.DietPlanId });
        return Result<FeedingTaskDto>.Success(MapTask(task, itemsByPlan));
    }

    // Reports

    public async Task<Result<List<ConsumptionTrendPointDto>>> GetConsumptionTrendAsync(Guid farmId, string period, DateTime? from, DateTime? to)
    {
        var normalizedPeriod = string.IsNullOrWhiteSpace(period) ? "day" : period.ToLowerInvariant();
        if (normalizedPeriod != "day" && normalizedPeriod != "week" && normalizedPeriod != "month")
            return Result<List<ConsumptionTrendPointDto>>.Validation("Period must be one of: day, week, month");

        var range = ResolveRange(from, to);
        if (range == null)
            return Result<List<ConsumptionTrendPointDto>>.Validation("From date must be before or equal to To date");

        var records = await LoadRecordsInRangeAsync(farmId, range.Value.From, range.Value.To);

        var points = records
            .GroupBy(r => GetBucketStart(r.FedAt, normalizedPeriod))
            .OrderBy(g => g.Key)
            .Select(g => new ConsumptionTrendPointDto
            {
                PeriodStart = g.Key,
                Label = FormatBucket(g.Key, normalizedPeriod),
                Quantity = g.Sum(r => r.Quantity),
                Cost = g.Sum(r => r.Quantity * r.UnitCost)
            })
            .ToList();

        return Result<List<ConsumptionTrendPointDto>>.Success(points);
    }

    public async Task<Result<List<FeedTypeBreakdownDto>>> GetConsumptionByFeedTypeAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var range = ResolveRange(from, to);
        if (range == null)
            return Result<List<FeedTypeBreakdownDto>>.Validation("From date must be before or equal to To date");

        var records = await LoadRecordsInRangeAsync(farmId, range.Value.From, range.Value.To);

        var breakdown = records
            .GroupBy(r => r.FeedTypeId)
            .Select(g =>
            {
                var first = g.First();
                return new
                {
                    FeedTypeId = g.Key,
                    first.FeedType.Name,
                    first.FeedType.Unit,
                    Quantity = g.Sum(r => r.Quantity),
                    Cost = g.Sum(r => r.Quantity * r.UnitCost)
                };
            })
            .OrderByDescending(x => x.Cost)
            .ToList();

        var totalCost = breakdown.Sum(x => x.Cost);

        return Result<List<FeedTypeBreakdownDto>>.Success(breakdown.Select(x => new FeedTypeBreakdownDto
        {
            FeedTypeId = x.FeedTypeId,
            FeedTypeName = x.Name,
            UnitName = x.Unit.ToString(),
            Quantity = x.Quantity,
            Cost = x.Cost,
            SharePercent = totalCost > 0 ? Math.Round(x.Cost / totalCost * 100, 2) : 0
        }).ToList());
    }

    public async Task<Result<List<AnimalConsumptionDto>>> GetConsumptionByAnimalAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var range = ResolveRange(from, to);
        if (range == null)
            return Result<List<AnimalConsumptionDto>>.Validation("From date must be before or equal to To date");

        var records = await LoadRecordsInRangeAsync(farmId, range.Value.From, range.Value.To);

        var byAnimal = records
            .Where(r => r.AnimalId.HasValue)
            .GroupBy(r => r.AnimalId!.Value)
            .Select(g => new AnimalConsumptionDto
            {
                AnimalId = g.Key,
                TagNumber = g.First().Animal!.TagNumber,
                Name = g.First().Animal!.Name,
                Quantity = g.Sum(r => r.Quantity),
                Cost = g.Sum(r => r.Quantity * r.UnitCost)
            })
            .OrderByDescending(x => x.Cost)
            .ToList();

        return Result<List<AnimalConsumptionDto>>.Success(byAnimal);
    }

    public async Task<Result<List<LocationConsumptionDto>>> GetConsumptionByLocationAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var range = ResolveRange(from, to);
        if (range == null)
            return Result<List<LocationConsumptionDto>>.Validation("From date must be before or equal to To date");

        var records = await LoadRecordsInRangeAsync(farmId, range.Value.From, range.Value.To);

        var byLocation = records
            .Where(r => r.LocationId.HasValue)
            .GroupBy(r => r.LocationId!.Value)
            .Select(g => new LocationConsumptionDto
            {
                LocationId = g.Key,
                LocationName = g.First().Location!.Name,
                Quantity = g.Sum(r => r.Quantity),
                Cost = g.Sum(r => r.Quantity * r.UnitCost)
            })
            .OrderByDescending(x => x.Cost)
            .ToList();

        return Result<List<LocationConsumptionDto>>.Success(byLocation);
    }

    public async Task<Result<FeedCostSummaryDto>> GetCostSummaryAsync(Guid farmId, DateTime? from, DateTime? to)
    {
        var range = ResolveRange(from, to);
        if (range == null)
            return Result<FeedCostSummaryDto>.Validation("From date must be before or equal to To date");

        var types = await _context.FeedTypes
            .Where(ft => ft.FarmId == farmId)
            .ToListAsync();
        var movements = await _context.FeedStockMovements
            .Where(m => m.FarmId == farmId)
            .ToListAsync();
        var records = await LoadRecordsInRangeAsync(farmId, range.Value.From, range.Value.To);

        var purchasedInRange = movements
            .Where(m => m.MovementType == StockMovementType.Purchase &&
                        m.MovementDate >= range.Value.From && m.MovementDate < range.Value.To.AddDays(1))
            .ToList();

        var summary = new FeedCostSummaryDto
        {
            From = range.Value.From,
            To = range.Value.To,
            TotalConsumedQuantity = records.Sum(r => r.Quantity),
            TotalConsumedCost = records.Sum(r => r.Quantity * r.UnitCost),
            TotalPurchasedQuantity = purchasedInRange.Sum(m => m.Quantity),
            TotalPurchasedCost = purchasedInRange.Sum(m => m.TotalCost ?? 0)
        };

        foreach (var type in types)
        {
            var typeMovements = movements.Where(m => m.FeedTypeId == type.Id).ToList();
            var stock = typeMovements.Sum(m => SignedQuantity(m.MovementType, m.Quantity));
            summary.CurrentInventoryValue += stock * type.CostPerUnit;
        }

        return Result<FeedCostSummaryDto>.Success(summary);
    }

    // Helpers

    private static string? ValidateDetails(string name, decimal costPerUnit)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Feed type name is required";
        if (name.Length > 100)
            return "Feed type name cannot exceed 100 characters";
        if (costPerUnit < 0)
            return "Cost per unit cannot be negative";
        return null;
    }

    private async Task<decimal> ComputeStockAsync(Guid farmId, Guid feedTypeId)
    {
        var movements = await _context.FeedStockMovements
            .Where(m => m.FarmId == farmId && m.FeedTypeId == feedTypeId)
            .Select(m => new { m.MovementType, m.Quantity })
            .ToListAsync();

        return movements.Sum(m => SignedQuantity(m.MovementType, m.Quantity));
    }

    private static decimal SignedQuantity(StockMovementType type, decimal quantity) => type switch
    {
        StockMovementType.Purchase => quantity,
        StockMovementType.Consumption => -quantity,
        StockMovementType.Adjustment => quantity,
        _ => 0m
    };

    private static string? ValidateDietPlanDetails(string name, decimal? minWeightKg, decimal? maxWeightKg)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Diet plan name is required";
        if (name.Length > 100)
            return "Diet plan name cannot exceed 100 characters";
        if (minWeightKg.HasValue && minWeightKg.Value < 0)
            return "MinWeightKg cannot be negative";
        if (maxWeightKg.HasValue && maxWeightKg.Value < 0)
            return "MaxWeightKg cannot be negative";
        if (minWeightKg.HasValue && maxWeightKg.HasValue && minWeightKg.Value > maxWeightKg.Value)
            return "MinWeightKg must be less than or equal to MaxWeightKg";
        return null;
    }

    private async Task<Error?> ValidateDietPlanCriteriaAsync(Guid farmId, Guid? animalTypeId, Guid? breedId, Guid? ageCategoryId)
    {
        if (animalTypeId.HasValue &&
            !await _context.AnimalTypes.AnyAsync(at => at.Id == animalTypeId.Value && at.FarmId == farmId))
            return Error.NotFound("Animal type not found");

        if (breedId.HasValue)
        {
            var breed = await _context.Breeds
                .Include(b => b.AnimalType)
                .FirstOrDefaultAsync(b => b.Id == breedId.Value && b.AnimalType.FarmId == farmId);
            if (breed == null)
                return Error.NotFound("Breed not found");
            if (animalTypeId.HasValue && breed.AnimalTypeId != animalTypeId.Value)
                return Error.Validation("Breed does not belong to the specified animal type");
        }

        if (ageCategoryId.HasValue &&
            !await _context.AgeCategories.AnyAsync(ac => ac.Id == ageCategoryId.Value && ac.FarmId == farmId))
            return Error.NotFound("Age category not found");

        return null;
    }

    /// <summary>Validates and attaches items to a plan (without saving). Returns an Error on failure.</summary>
    private async Task<Error?> AddItemsToPlanInternalAsync(
        Guid farmId, DietPlan plan, List<AddDietPlanItemRequest> requests, Guid? userId, DateTime now)
    {
        foreach (var request in requests)
        {
            if (request.QuantityPerFeeding <= 0)
                return Error.Validation("Quantity per feeding must be greater than zero");

            var feedType = await _context.FeedTypes
                .FirstOrDefaultAsync(ft => ft.Id == request.FeedTypeId && ft.FarmId == farmId);
            if (feedType == null)
                return Error.NotFound("Feed type not found");

            var alreadyInRequest = plan.Items.Any(i => i.FeedTypeId == request.FeedTypeId) ||
                                   requests.Count(r => r.FeedTypeId == request.FeedTypeId) > 1;
            if (alreadyInRequest)
                return Error.Conflict($"Feed type '{feedType.Name}' is already in the diet plan");

            plan.Items.Add(new DietPlanItem
            {
                Id = Guid.NewGuid(),
                DietPlanId = plan.Id,
                FeedTypeId = feedType.Id,
                QuantityPerFeeding = Math.Round(request.QuantityPerFeeding, 2),
                CreatedAt = now,
                CreatedBy = userId
            });
        }

        return null;
    }

    /// <summary>
    /// How many animals each plan targets, for every plan in a single pass over the farm.
    ///
    /// <para>
    /// Replaces a per-plan count. The candidates and their latest weights are the same rows for
    /// every plan, so they are loaded once and each plan's filtering is done in memory — the
    /// same predicate, the same result, one query instead of one per plan.
    /// </para>
    /// </summary>
    private async Task<Dictionary<Guid, int>> CountMatchingAnimalsByPlanAsync(
        Guid farmId, IReadOnlyList<DietPlan> plans)
    {
        var counts = new Dictionary<Guid, int>();
        if (plans.Count == 0)
            return counts;

        var candidates = await _context.Animals.AsNoTracking()
            .Where(a => a.FarmId == farmId && !a.IsDeleted)
            .Select(a => new { a.Id, a.AnimalTypeId, a.BreedId, a.AgeCategoryId })
            .ToListAsync();

        // Only fetched when a plan actually asks about weight, so a farm whose plans have no
        // weight range still takes no extra round trip.
        var latestWeightByAnimal = new Dictionary<Guid, decimal>();
        if (candidates.Count > 0 && plans.Any(p => p.MinWeightKg.HasValue || p.MaxWeightKg.HasValue))
        {
            var ids = candidates.Select(c => c.Id).ToList();

            latestWeightByAnimal = (await _context.WeightRecords.AsNoTracking()
                    .Where(w => ids.Contains(w.AnimalId))
                    .OrderBy(w => w.RecordedAt)
                    .Select(w => new { w.AnimalId, w.WeightKg })
                    .ToListAsync())
                .GroupBy(w => w.AnimalId)
                .ToDictionary(g => g.Key, g => g.Last().WeightKg);
        }

        foreach (var plan in plans)
        {
            var matching = candidates.Where(a =>
                (!plan.AnimalTypeId.HasValue || a.AnimalTypeId == plan.AnimalTypeId.Value) &&
                (!plan.BreedId.HasValue || a.BreedId == plan.BreedId.Value) &&
                (!plan.AgeCategoryId.HasValue || a.AgeCategoryId == plan.AgeCategoryId.Value)).ToList();

            if (matching.Count == 0)
            {
                counts[plan.Id] = 0;
                continue;
            }

            if (!plan.MinWeightKg.HasValue && !plan.MaxWeightKg.HasValue)
            {
                counts[plan.Id] = matching.Count;
                continue;
            }

            counts[plan.Id] = matching.Count(c =>
                latestWeightByAnimal.ContainsKey(c.Id) &&
                (!plan.MinWeightKg.HasValue || latestWeightByAnimal[c.Id] >= plan.MinWeightKg.Value) &&
                (!plan.MaxWeightKg.HasValue || latestWeightByAnimal[c.Id] <= plan.MaxWeightKg.Value));
        }

        return counts;
    }

    private async Task<Dictionary<Guid, List<DietPlanItem>>> LoadPlanItemsAsync(List<Guid> planIds)
    {
        if (planIds.Count == 0)
            return new Dictionary<Guid, List<DietPlanItem>>();

        var items = await _context.DietPlanItems
            .Include(i => i.FeedType)
            .Where(i => planIds.Contains(i.DietPlanId))
            .ToListAsync();

        return items.GroupBy(i => i.DietPlanId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static (DateTime From, DateTime To)? ResolveRange(DateTime? from, DateTime? to)
    {
        var effectiveTo = (to ?? DateTime.UtcNow).Date;
        var effectiveFrom = (from ?? effectiveTo.AddDays(-30)).Date;
        return effectiveFrom > effectiveTo ? null : (effectiveFrom, effectiveTo);
    }

    private async Task<List<FeedRecord>> LoadRecordsInRangeAsync(Guid farmId, DateTime from, DateTime to)
    {
        return await _context.FeedRecords
            .Include(r => r.FeedType)
            .Include(r => r.Animal)
            .Include(r => r.Location)
            .Where(r => r.FarmId == farmId && r.FedAt >= from && r.FedAt < to.AddDays(1))
            .ToListAsync();
    }

    private static DateTime GetBucketStart(DateTime date, string period) => period switch
    {
        "week" => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        "month" => new DateTime(date.Year, date.Month, 1),
        _ => date.Date
    };

    private static string FormatBucket(DateTime bucketStart, string period) => period == "month"
        ? bucketStart.ToString("yyyy-MM", CultureInfo.InvariantCulture)
        : bucketStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseTimeOfDay(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (!TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out time))
            return false;
        return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    }

    private static string FormatTime(TimeSpan time) => $"{time.Hours:D2}:{time.Minutes:D2}";

    private static string UnitAbbreviation(FeedUnit unit) => unit switch
    {
        FeedUnit.Kilogram => "kg",
        FeedUnit.Gram => "g",
        FeedUnit.Ton => "t",
        FeedUnit.Liter => "L",
        FeedUnit.Bale => "bale",
        FeedUnit.Bag => "bag",
        _ => "unit"
    };

    private static FeedTypeDto MapFeedType(FeedType feedType) => new()
    {
        Id = feedType.Id,
        Name = feedType.Name,
        Category = feedType.Category,
        CategoryName = feedType.Category.ToString(),
        Unit = feedType.Unit,
        UnitName = feedType.Unit.ToString(),
        CostPerUnit = feedType.CostPerUnit,
        Notes = feedType.Notes
    };

    private static StockMovementDto MapMovement(FeedStockMovement movement, FeedType feedType) => new()
    {
        Id = movement.Id,
        FeedTypeId = movement.FeedTypeId,
        FeedTypeName = feedType.Name,
        MovementType = movement.MovementType,
        MovementTypeName = movement.MovementType.ToString(),
        Quantity = movement.Quantity,
        SignedQuantity = SignedQuantity(movement.MovementType, movement.Quantity),
        UnitCost = movement.UnitCost,
        TotalCost = movement.TotalCost,
        Supplier = movement.Supplier,
        Notes = movement.Notes,
        MovementDate = movement.MovementDate
    };

    private static FeedRecordDto MapFeedRecord(FeedRecord record) => new()
    {
        Id = record.Id,
        FeedTypeId = record.FeedTypeId,
        FeedTypeName = record.FeedType.Name,
        UnitName = record.FeedType.Unit.ToString(),
        AnimalId = record.AnimalId,
        AnimalTagNumber = record.Animal?.TagNumber,
        AnimalName = record.Animal?.Name,
        LocationId = record.LocationId,
        LocationName = record.Location?.Name,
        Quantity = record.Quantity,
        UnitCost = record.UnitCost,
        TotalCost = Math.Round(record.Quantity * record.UnitCost, 2),
        FedAt = record.FedAt,
        Notes = record.Notes
    };

    private static DietPlanDto MapDietPlan(DietPlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        AnimalTypeId = plan.AnimalTypeId,
        AnimalTypeName = plan.AnimalType?.Name,
        BreedId = plan.BreedId,
        BreedName = plan.Breed?.Name,
        AgeCategoryId = plan.AgeCategoryId,
        AgeCategoryName = plan.AgeCategory?.Name,
        MinWeightKg = plan.MinWeightKg,
        MaxWeightKg = plan.MaxWeightKg,
        IsActive = plan.IsActive,
        Notes = plan.Notes,
        Items = plan.Items.Select(MapItem).ToList(),
        ScheduleCount = plan.Schedules.Count
    };

    private static DietPlanItemDto MapItem(DietPlanItem item) => new()
    {
        Id = item.Id,
        FeedTypeId = item.FeedTypeId,
        FeedTypeName = item.FeedType.Name,
        UnitName = item.FeedType.Unit.ToString(),
        QuantityPerFeeding = item.QuantityPerFeeding
    };

    private static FeedingScheduleDto MapSchedule(FeedingSchedule schedule) => new()
    {
        Id = schedule.Id,
        DietPlanId = schedule.DietPlanId,
        DietPlanName = schedule.DietPlan.Name,
        TimeOfDay = FormatTime(schedule.TimeOfDay),
        Label = schedule.Label,
        IsActive = schedule.IsActive
    };

    private static FeedingTaskDto MapTask(FeedingTask task, Dictionary<Guid, List<DietPlanItem>> itemsByPlan) => new()
    {
        Id = task.Id,
        FeedingScheduleId = task.FeedingScheduleId,
        DietPlanId = task.DietPlanId,
        DietPlanName = task.DietPlan.Name,
        TaskDate = task.TaskDate,
        TimeOfDay = FormatTime(task.TimeOfDay),
        Status = task.Status,
        StatusName = task.Status.ToString(),
        TargetAnimalCount = task.TargetAnimalCount,
        Items = itemsByPlan.TryGetValue(task.DietPlanId, out var items)
            ? items.Select(MapItem).ToList()
            : new List<DietPlanItemDto>(),
        CompletedAt = task.CompletedAt,
        Notes = task.Notes
    };
}
