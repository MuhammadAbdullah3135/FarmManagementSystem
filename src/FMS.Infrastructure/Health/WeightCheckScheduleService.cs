using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Health;

public class WeightCheckScheduleService : IWeightCheckScheduleService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public WeightCheckScheduleService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // ── CRUD ─────────────────────────────────────────────

    public async Task<Result<PagedResult<WeightCheckScheduleDto>>> GetSchedulesAsync(Guid farmId, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

        var query = _context.WeightCheckSchedules
            .AsNoTracking()
            .Where(s => s.FarmId == farmId);

        var totalCount = await query.CountAsync();

        var schedules = await query
            .OrderBy(s => s.RecurrenceDays)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new WeightCheckScheduleDto
            {
                Id = s.Id,
                AnimalTypeId = s.AnimalTypeId,
                AnimalTypeName = s.AnimalType != null ? s.AnimalType.Name : null,
                BreedId = s.BreedId,
                BreedName = s.Breed != null ? s.Breed.Name : null,
                AgeCategoryId = s.AgeCategoryId,
                AgeCategoryName = s.AgeCategory != null ? s.AgeCategory.Name : null,
                RecurrenceDays = s.RecurrenceDays,
                IsActive = s.IsActive,
                Notes = s.Notes,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();

        return Result<PagedResult<WeightCheckScheduleDto>>.Success(new PagedResult<WeightCheckScheduleDto>
        {
            Items = schedules,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<WeightCheckScheduleDto>> CreateScheduleAsync(Guid farmId, CreateWeightCheckScheduleRequest request)
    {
        if (request.RecurrenceDays <= 0)
            return Result<WeightCheckScheduleDto>.Validation("Recurrence interval must be greater than zero");

        if (request.AnimalTypeId.HasValue)
        {
            var animalType = await _context.AnimalTypes
                .FirstOrDefaultAsync(a => a.Id == request.AnimalTypeId.Value && a.FarmId == farmId);
            if (animalType == null)
                return Result<WeightCheckScheduleDto>.Validation("Animal type not found in this farm");
        }

        if (request.BreedId.HasValue)
        {
            var breed = await _context.Breeds
                .FirstOrDefaultAsync(b => b.Id == request.BreedId.Value);
            if (breed == null)
                return Result<WeightCheckScheduleDto>.Validation("Breed not found");
        }

        if (request.AgeCategoryId.HasValue)
        {
            var ageCategory = await _context.AgeCategories
                .FirstOrDefaultAsync(a => a.Id == request.AgeCategoryId.Value && a.FarmId == farmId);
            if (ageCategory == null)
                return Result<WeightCheckScheduleDto>.Validation("Age category not found in this farm");
        }

        var userId = _currentUser.GetUserId();

        var schedule = new WeightCheckSchedule
        {
            FarmId = farmId,
            AnimalTypeId = request.AnimalTypeId,
            BreedId = request.BreedId,
            AgeCategoryId = request.AgeCategoryId,
            RecurrenceDays = request.RecurrenceDays,
            IsActive = request.IsActive,
            Notes = request.Notes?.Trim(),
            CreatedBy = userId
        };

        _context.WeightCheckSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        return Result<WeightCheckScheduleDto>.Success(new WeightCheckScheduleDto
        {
            Id = schedule.Id,
            AnimalTypeId = schedule.AnimalTypeId,
            AnimalTypeName = null,
            BreedId = schedule.BreedId,
            BreedName = null,
            AgeCategoryId = schedule.AgeCategoryId,
            AgeCategoryName = null,
            RecurrenceDays = schedule.RecurrenceDays,
            IsActive = schedule.IsActive,
            Notes = schedule.Notes,
            CreatedAt = schedule.CreatedAt
        });
    }

    public async Task<Result<WeightCheckScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateWeightCheckScheduleRequest request)
    {
        var schedule = await _context.WeightCheckSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.FarmId == farmId);

        if (schedule == null)
            return Result<WeightCheckScheduleDto>.NotFound("Weight check schedule not found");

        if (request.RecurrenceDays <= 0)
            return Result<WeightCheckScheduleDto>.Validation("Recurrence interval must be greater than zero");

        if (request.AnimalTypeId.HasValue)
        {
            var animalType = await _context.AnimalTypes
                .FirstOrDefaultAsync(a => a.Id == request.AnimalTypeId.Value && a.FarmId == farmId);
            if (animalType == null)
                return Result<WeightCheckScheduleDto>.Validation("Animal type not found in this farm");
        }

        if (request.BreedId.HasValue)
        {
            var breed = await _context.Breeds
                .FirstOrDefaultAsync(b => b.Id == request.BreedId.Value);
            if (breed == null)
                return Result<WeightCheckScheduleDto>.Validation("Breed not found");
        }

        if (request.AgeCategoryId.HasValue)
        {
            var ageCategory = await _context.AgeCategories
                .FirstOrDefaultAsync(a => a.Id == request.AgeCategoryId.Value && a.FarmId == farmId);
            if (ageCategory == null)
                return Result<WeightCheckScheduleDto>.Validation("Age category not found in this farm");
        }

        var userId = _currentUser.GetUserId();

        schedule.AnimalTypeId = request.AnimalTypeId;
        schedule.BreedId = request.BreedId;
        schedule.AgeCategoryId = request.AgeCategoryId;
        schedule.RecurrenceDays = request.RecurrenceDays;
        schedule.IsActive = request.IsActive;
        schedule.Notes = request.Notes?.Trim();
        schedule.ModifiedAt = DateTime.UtcNow;
        schedule.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        return Result<WeightCheckScheduleDto>.Success(new WeightCheckScheduleDto
        {
            Id = schedule.Id,
            AnimalTypeId = schedule.AnimalTypeId,
            AnimalTypeName = null,
            BreedId = schedule.BreedId,
            BreedName = null,
            AgeCategoryId = schedule.AgeCategoryId,
            AgeCategoryName = null,
            RecurrenceDays = schedule.RecurrenceDays,
            IsActive = schedule.IsActive,
            Notes = schedule.Notes,
            CreatedAt = schedule.CreatedAt
        });
    }

    public async Task<Result> DeleteScheduleAsync(Guid farmId, Guid id)
    {
        var schedule = await _context.WeightCheckSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.FarmId == farmId);

        if (schedule == null)
            return Result.NotFound("Weight check schedule not found");

        _context.WeightCheckSchedules.Remove(schedule);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // ── Status ───────────────────────────────────────────

    public async Task<Result<List<WeightCheckStatusDto>>> GetWeightCheckStatusAsync(Guid farmId)
    {
        var statuses = await ComputeWeightCheckStatusesAsync(farmId);
        return Result<List<WeightCheckStatusDto>>.Success(statuses);
    }

    public async Task<Result<List<WeightCheckStatusDto>>> GetOverdueWeightChecksAsync(Guid farmId)
    {
                var all = await ComputeWeightCheckStatusesAsync(farmId);
        var overdue = all.Where(s => s.Status == WeightCheckStatusType.Overdue || s.Status == WeightCheckStatusType.Due).ToList();
        return Result<List<WeightCheckStatusDto>>.Success(overdue);
    }

    private async Task<List<WeightCheckStatusDto>> ComputeWeightCheckStatusesAsync(Guid farmId)
    {
        var schedules = await _context.WeightCheckSchedules
            .AsNoTracking()
            .Where(s => s.FarmId == farmId && s.IsActive)
            .ToListAsync();

        var animals = await _context.Animals
            .AsNoTracking()
            .Where(a => a.FarmId == farmId)
            .ToListAsync();

        var results = new List<WeightCheckStatusDto>();
        var today = DateTime.UtcNow.Date;
        var userId = _currentUser.GetUserId();

        foreach (var schedule in schedules)
        {
            var matchingAnimals = animals.Where(a =>
                (!schedule.AnimalTypeId.HasValue || a.AnimalTypeId == schedule.AnimalTypeId) &&
                (!schedule.BreedId.HasValue || a.BreedId == schedule.BreedId) &&
                (!schedule.AgeCategoryId.HasValue || a.AgeCategoryId == schedule.AgeCategoryId)
            ).ToList();

            foreach (var animal in matchingAnimals)
            {
                var lastRecord = await _context.WeightRecords
                    .AsNoTracking()
                    .Where(wr => wr.AnimalId == animal.Id)
                    .OrderByDescending(wr => wr.RecordedAt)
                    .FirstOrDefaultAsync();

                var lastDate = lastRecord?.RecordedAt;
                if (!lastDate.HasValue)
                    lastDate = animal.CreatedAt;

                var nextDue = lastDate.Value.AddDays(schedule.RecurrenceDays);
                var daysUntilDue = (nextDue.Date - today).Days;

                WeightCheckStatusType status;
                if (daysUntilDue < 0)
                    status = WeightCheckStatusType.Overdue;
                else if (daysUntilDue <= 7)
                    status = WeightCheckStatusType.Due;
                else
                    status = WeightCheckStatusType.Upcoming;

                results.Add(new WeightCheckStatusDto
                {
                    AnimalId = animal.Id,
                    AnimalTagNumber = animal.TagNumber,
                    AnimalName = animal.Name,
                    LastWeightDate = lastRecord?.RecordedAt,
                    NextDueDate = nextDue,
                    Status = status,
                    StatusName = status.ToString(),
                    DaysUntilDue = daysUntilDue
                });

                if (status == WeightCheckStatusType.Due || status == WeightCheckStatusType.Overdue)
                {
                    var taskTitle = "Weight check due: " + animal.TagNumber;
                    var existingTask = await _context.FarmTasks
                        .AnyAsync(t =>
                            t.FarmId == farmId &&
                            t.AnimalId == animal.Id &&
                            t.Title == taskTitle &&
                            t.Status != FarmTaskStatus.Completed &&
                            t.Status != FarmTaskStatus.Cancelled);

                    if (!existingTask)
                    {
                        _context.FarmTasks.Add(new FarmTask
                        {
                            FarmId = farmId,
                            Title = taskTitle,
                            Description = "Scheduled every " + schedule.RecurrenceDays + " days. Next due: " + nextDue.ToString("yyyy-MM-dd"),
                            Priority = FarmTaskPriority.Medium,
                            Status = FarmTaskStatus.Pending,
                            DueDate = nextDue,
                            AnimalId = animal.Id,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = userId
                        });
                    }
                }
            }
        }

        await _context.SaveChangesAsync();
        return results.OrderBy(s => s.DaysUntilDue).ToList();
    }
}
