using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Common;
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
        var statuses = await ComputeWeightCheckStatusesAsync(farmId, null);
        return Result<List<WeightCheckStatusDto>>.Success(statuses.Select(e => e.Status).ToList());
    }

    public async Task<Result<DeltaResult<WeightCheckStatusDto>>> GetWeightCheckStatusAsync(Guid farmId, DateTime? updatedSince)
    {
        var cursor = DateTime.UtcNow;

        if (updatedSince.HasValue && !DeltaCursor.IsAnswerable(updatedSince, cursor))
            return Result<DeltaResult<WeightCheckStatusDto>>.Success(DeltaResult<WeightCheckStatusDto>.FullSyncRequired(cursor));

        if (updatedSince is { } since)
        {
            // An edited or removed schedule can take entries away, and the delta has no id for
            // an entry that no longer exists — so the honest answer is a full read. An added
            // one can only create entries, which the changed-input filter below already catches.
            var schedulesChanged = await ChangeTombstones.AnyChangedSinceAsync<WeightCheckSchedule>(
                _context, farmId, since, new[] { AuditAction.Update, AuditAction.Delete });

            if (schedulesChanged)
                return Result<DeltaResult<WeightCheckStatusDto>>.Success(DeltaResult<WeightCheckStatusDto>.FullSyncRequired(cursor));
        }

        var entries = await ComputeWeightCheckStatusesAsync(farmId, updatedSince);

        var byDueDate = entries.OrderBy(e => e.Status.DaysUntilDue).Select(e => e.Status).ToList();

        var delta = new DeltaResult<WeightCheckStatusDto>
        {
            Items = byDueDate,
            Page = 1,
            PageSize = byDueDate.Count,
            TotalCount = byDueDate.Count,
            Cursor = cursor,
            // An animal that was deleted takes its entries with it, and nothing is left to
            // return — the id is how the client knows to drop the row it still holds.
            DeletedIds = updatedSince is { } deletedSince
                ? await _context.Animals
                    .AsNoTracking()
                    .Where(a => a.FarmId == farmId && a.IsDeleted && (a.ModifiedAt ?? a.CreatedAt) > deletedSince)
                    .Select(a => a.Id)
                    .ToListAsync()
                : new List<Guid>()
        };

        return Result<DeltaResult<WeightCheckStatusDto>>.Success(delta);
    }

    public async Task<Result<List<WeightCheckStatusDto>>> GetOverdueWeightChecksAsync(Guid farmId)
    {
        var all = await ComputeWeightCheckStatusesAsync(farmId, null);
        var overdue = all
            .Where(e => e.Status.Status == WeightCheckStatusType.Overdue || e.Status.Status == WeightCheckStatusType.Due)
            .OrderBy(e => e.Status.DaysUntilDue)
            .Select(e => e.Status)
            .ToList();
        return Result<List<WeightCheckStatusDto>>.Success(overdue);
    }

    /// <summary>One computed entry, with the timestamp of the newest input behind it.</summary>
    private sealed record ComputedWeightCheck(WeightCheckStatusDto Status, DateTime ChangedAt);

    /// <summary>
    /// The projection itself. <paramref name="changedSince"/> turns it into a delta by keeping
    /// only the entries one of whose inputs was touched after that moment.
    /// </summary>
    private async Task<List<ComputedWeightCheck>> ComputeWeightCheckStatusesAsync(Guid farmId, DateTime? changedSince)
    {
        var schedules = await _context.WeightCheckSchedules
            .AsNoTracking()
            .Where(s => s.FarmId == farmId && s.IsActive)
            .ToListAsync();

        // Deleted animals are excluded: the status of an animal that is gone is not work anyone
        // can do, and its weight-check task should not be re-created on every read.
        var animals = await _context.Animals
            .AsNoTracking()
            .Where(a => a.FarmId == farmId && !a.IsDeleted)
            .ToListAsync();

        var results = new List<ComputedWeightCheck>();
        var today = DateTime.UtcNow.Date;
        var userId = _currentUser.GetUserId();

        foreach (var schedule in schedules)
        {
            var scheduleChangedAt = schedule.ModifiedAt ?? schedule.CreatedAt;

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

                var animalChangedAt = animal.ModifiedAt ?? animal.CreatedAt;
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

                var changedAt = Later(
                    Later(animalChangedAt, scheduleChangedAt),
                    lastRecord is null ? animal.CreatedAt : lastRecord.ModifiedAt ?? lastRecord.CreatedAt);

                if (changedSince is { } sinceValue && changedAt <= sinceValue)
                    continue;

                results.Add(new ComputedWeightCheck(new WeightCheckStatusDto
                {
                    AnimalId = animal.Id,
                    AnimalTagNumber = animal.TagNumber,
                    AnimalName = animal.Name,
                    LastWeightDate = lastRecord?.RecordedAt,
                    NextDueDate = nextDue,
                    Status = status,
                    StatusName = status.ToString(),
                    DaysUntilDue = daysUntilDue
                }, changedAt));

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
        return results;
    }

    private static DateTime Later(DateTime left, DateTime right) => left >= right ? left : right;
}
