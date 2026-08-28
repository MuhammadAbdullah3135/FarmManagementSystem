using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Health;

public class VaccineService : IVaccineService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public VaccineService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    // ── VaccineType CRUD ──────────────────────────────────

    public async Task<Result<PagedResult<VaccineTypeListItemDto>>> GetVaccineTypesAsync(Guid farmId, int page, int pageSize, string? search)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

        var query = _context.VaccineTypes
            .AsNoTracking()
            .Where(v => v.FarmId == farmId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(v => v.Name.Contains(term));
        }

        var totalCount = await query.CountAsync();

        var types = await query
            .OrderBy(v => v.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new VaccineTypeListItemDto
            {
                Id = v.Id,
                Name = v.Name,
                DefaultDosage = v.DefaultDosage,
                LinkedMedicineId = v.LinkedMedicineId,
                LinkedMedicineName = v.LinkedMedicine != null ? v.LinkedMedicine.Name : null,
                VaccinationCount = v.VaccinationRecords.Count,
                ScheduleCount = v.VaccinationSchedules.Count
            })
            .ToListAsync();

        return Result<PagedResult<VaccineTypeListItemDto>>.Success(new PagedResult<VaccineTypeListItemDto>
        {
            Items = types,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<VaccineTypeDto>> GetVaccineTypeByIdAsync(Guid farmId, Guid id)
    {
        var vax = await _context.VaccineTypes
            .AsNoTracking()
            .Where(v => v.FarmId == farmId && v.Id == id)
            .Select(v => new VaccineTypeDto
            {
                Id = v.Id,
                Name = v.Name,
                DefaultDosage = v.DefaultDosage,
                Notes = v.Notes,
                LinkedMedicineId = v.LinkedMedicineId,
                LinkedMedicineName = v.LinkedMedicine != null ? v.LinkedMedicine.Name : null,
                VaccinationCount = v.VaccinationRecords.Count,
                ScheduleCount = v.VaccinationSchedules.Count,
                CreatedAt = v.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (vax == null)
            return Result<VaccineTypeDto>.NotFound("Vaccine type not found");

        return Result<VaccineTypeDto>.Success(vax);
    }

    public async Task<Result<VaccineTypeDto>> CreateVaccineTypeAsync(Guid farmId, CreateVaccineTypeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<VaccineTypeDto>.Validation("Vaccine name is required");

        var exists = await _context.VaccineTypes
            .AnyAsync(v => v.FarmId == farmId && v.Name == request.Name);
        if (exists)
            return Result<VaccineTypeDto>.Conflict("A vaccine type with this name already exists");

        if (request.LinkedMedicineId.HasValue)
        {
            var medicine = await _context.Medicines
                .FirstOrDefaultAsync(m => m.Id == request.LinkedMedicineId.Value && m.FarmId == farmId);
            if (medicine == null)
                return Result<VaccineTypeDto>.Validation("Linked medicine not found in this farm");
        }

        var userId = _currentUser.GetUserId();

        var vax = new VaccineType
        {
            FarmId = farmId,
            Name = request.Name.Trim(),
            DefaultDosage = request.DefaultDosage?.Trim(),
            Notes = request.Notes?.Trim(),
            LinkedMedicineId = request.LinkedMedicineId,
            CreatedBy = userId
        };

        _context.VaccineTypes.Add(vax);
        await _context.SaveChangesAsync();

        return Result<VaccineTypeDto>.Success(new VaccineTypeDto
        {
            Id = vax.Id,
            Name = vax.Name,
            DefaultDosage = vax.DefaultDosage,
            Notes = vax.Notes,
            LinkedMedicineId = vax.LinkedMedicineId,
            VaccinationCount = 0,
            ScheduleCount = 0,
            CreatedAt = vax.CreatedAt
        });
    }

    public async Task<Result<VaccineTypeDto>> UpdateVaccineTypeAsync(Guid farmId, Guid id, UpdateVaccineTypeRequest request)
    {
        var vax = await _context.VaccineTypes
            .FirstOrDefaultAsync(v => v.FarmId == farmId && v.Id == id);

        if (vax == null)
            return Result<VaccineTypeDto>.NotFound("Vaccine type not found");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<VaccineTypeDto>.Validation("Vaccine name is required");

        var nameExists = await _context.VaccineTypes
            .AnyAsync(v => v.FarmId == farmId && v.Name == request.Name && v.Id != id);
        if (nameExists)
            return Result<VaccineTypeDto>.Conflict("A vaccine type with this name already exists");

        if (request.LinkedMedicineId.HasValue)
        {
            var medicine = await _context.Medicines
                .FirstOrDefaultAsync(m => m.Id == request.LinkedMedicineId.Value && m.FarmId == farmId);
            if (medicine == null)
                return Result<VaccineTypeDto>.Validation("Linked medicine not found in this farm");
        }

        var userId = _currentUser.GetUserId();

        vax.Name = request.Name.Trim();
        vax.DefaultDosage = request.DefaultDosage?.Trim();
        vax.Notes = request.Notes?.Trim();
        vax.LinkedMedicineId = request.LinkedMedicineId;
        vax.ModifiedAt = DateTime.UtcNow;
        vax.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        var vaccCount = await _context.VaccinationRecords.CountAsync(vr => vr.VaccineTypeId == id);
        var schedCount = await _context.VaccinationSchedules.CountAsync(vs => vs.VaccineTypeId == id);

        return Result<VaccineTypeDto>.Success(new VaccineTypeDto
        {
            Id = vax.Id,
            Name = vax.Name,
            DefaultDosage = vax.DefaultDosage,
            Notes = vax.Notes,
            LinkedMedicineId = vax.LinkedMedicineId,
            VaccinationCount = vaccCount,
            ScheduleCount = schedCount,
            CreatedAt = vax.CreatedAt
        });
    }

    public async Task<Result> DeleteVaccineTypeAsync(Guid farmId, Guid id)
    {
        var vax = await _context.VaccineTypes
            .FirstOrDefaultAsync(v => v.FarmId == farmId && v.Id == id);

        if (vax == null)
            return Result.NotFound("Vaccine type not found");

        var hasRecords = await _context.VaccinationRecords.AnyAsync(vr => vr.VaccineTypeId == id);
        if (hasRecords)
            return Result.Validation("Cannot delete vaccine type with existing vaccination records");

        var hasSchedules = await _context.VaccinationSchedules.AnyAsync(vs => vs.VaccineTypeId == id);
        if (hasSchedules)
            return Result.Validation("Cannot delete vaccine type with existing schedules");

        _context.VaccineTypes.Remove(vax);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // ── VaccinationRecord CRUD ────────────────────────────

    public async Task<Result<PagedResult<VaccinationRecordListItemDto>>> GetVaccinationRecordsAsync(Guid farmId, VaccinationRecordListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = _context.VaccinationRecords
            .AsNoTracking()
            .Where(vr => vr.FarmId == farmId);

        if (filter.AnimalId.HasValue)
            query = query.Where(vr => vr.AnimalId == filter.AnimalId.Value);

        if (filter.VaccineTypeId.HasValue)
            query = query.Where(vr => vr.VaccineTypeId == filter.VaccineTypeId.Value);

        if (filter.FromDate.HasValue)
            query = query.Where(vr => vr.DateGiven >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(vr => vr.DateGiven <= filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(vr =>
                vr.Animal.TagNumber.Contains(term) ||
                (vr.Animal.Name != null && vr.Animal.Name.Contains(term)) ||
                vr.VaccineType.Name.Contains(term) ||
                (vr.VetName != null && vr.VetName.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderByDescending(vr => vr.DateGiven)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(vr => new VaccinationRecordListItemDto
            {
                Id = vr.Id,
                AnimalId = vr.AnimalId,
                AnimalTagNumber = vr.Animal.TagNumber,
                AnimalName = vr.Animal.Name,
                VaccineTypeId = vr.VaccineTypeId,
                VaccineTypeName = vr.VaccineType.Name,
                DateGiven = vr.DateGiven,
                VetName = vr.VetName,
                BatchNumber = vr.BatchNumber,
                Cost = vr.Cost
            })
            .ToListAsync();

        return Result<PagedResult<VaccinationRecordListItemDto>>.Success(new PagedResult<VaccinationRecordListItemDto>
        {
            Items = records,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<VaccinationRecordDto>> GetVaccinationRecordByIdAsync(Guid farmId, Guid id)
    {
        var record = await _context.VaccinationRecords
            .AsNoTracking()
            .Where(vr => vr.FarmId == farmId && vr.Id == id)
            .Select(vr => new VaccinationRecordDto
            {
                Id = vr.Id,
                FarmId = vr.FarmId,
                AnimalId = vr.AnimalId,
                AnimalTagNumber = vr.Animal.TagNumber,
                AnimalName = vr.Animal.Name,
                VaccineTypeId = vr.VaccineTypeId,
                VaccineTypeName = vr.VaccineType.Name,
                DateGiven = vr.DateGiven,
                VetName = vr.VetName,
                BatchNumber = vr.BatchNumber,
                Cost = vr.Cost,
                Notes = vr.Notes,
                CreatedAt = vr.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (record == null)
            return Result<VaccinationRecordDto>.NotFound("Vaccination record not found");

        return Result<VaccinationRecordDto>.Success(record);
    }

    public async Task<Result<PagedResult<VaccinationRecordListItemDto>>> GetVaccinationRecordsByAnimalAsync(Guid farmId, Guid animalId, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

        var query = _context.VaccinationRecords
            .AsNoTracking()
            .Where(vr => vr.FarmId == farmId && vr.AnimalId == animalId);

        var totalCount = await query.CountAsync();

        var records = await query
            .OrderByDescending(vr => vr.DateGiven)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(vr => new VaccinationRecordListItemDto
            {
                Id = vr.Id,
                AnimalId = vr.AnimalId,
                AnimalTagNumber = vr.Animal.TagNumber,
                AnimalName = vr.Animal.Name,
                VaccineTypeId = vr.VaccineTypeId,
                VaccineTypeName = vr.VaccineType.Name,
                DateGiven = vr.DateGiven,
                VetName = vr.VetName,
                BatchNumber = vr.BatchNumber,
                Cost = vr.Cost
            })
            .ToListAsync();

        return Result<PagedResult<VaccinationRecordListItemDto>>.Success(new PagedResult<VaccinationRecordListItemDto>
        {
            Items = records,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<VaccinationRecordDto>> CreateVaccinationRecordAsync(Guid farmId, CreateVaccinationRecordRequest request)
    {
        var animal = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == request.AnimalId && a.FarmId == farmId);
        if (animal == null)
            return Result<VaccinationRecordDto>.Validation("Animal not found in this farm");

        var vaxType = await _context.VaccineTypes
            .FirstOrDefaultAsync(v => v.Id == request.VaccineTypeId && v.FarmId == farmId);
        if (vaxType == null)
            return Result<VaccinationRecordDto>.Validation("Vaccine type not found in this farm");

        var userId = _currentUser.GetUserId();

        var record = new VaccinationRecord
        {
            FarmId = farmId,
            AnimalId = request.AnimalId,
            VaccineTypeId = request.VaccineTypeId,
            DateGiven = request.DateGiven == default ? DateTime.UtcNow : request.DateGiven,
            VetName = request.VetName?.Trim(),
            BatchNumber = request.BatchNumber?.Trim(),
            Cost = request.Cost,
            Notes = request.Notes?.Trim(),
            CreatedBy = userId
        };

        _context.VaccinationRecords.Add(record);

        // Timeline event
        var timelineEvent = new AnimalTimelineEvent
        {
            AnimalId = request.AnimalId,
            FarmId = farmId,
            EventType = TimelineEventTypes.VaccinationRecord,
            Title = $"Vaccination: {vaxType.Name}",
            Description = $"Given by {request.VetName ?? "Unknown"}",
            RelatedEntityId = record.Id,
            RelatedEntityType = nameof(VaccinationRecord),
            OccurredAt = record.DateGiven,
            CreatedBy = userId
        };
        _context.AnimalTimelineEvents.Add(timelineEvent);

        await _context.SaveChangesAsync();

        if (vaxType.LinkedMedicineId.HasValue && !await DeductLinkedMedicineAsync(farmId, vaxType.LinkedMedicineId.Value, userId, record.Id, record.DateGiven))
            return Result<VaccinationRecordDto>.Validation("Insufficient linked medicine stock");

        // Auto-create Expense when Cost > 0
        if (request.Cost > 0)
        {
            var expense = await AutoCreateVetExpenseAsync(farmId, request.Cost, request.VetName,
                $"Vaccination: {vaxType.Name}", record.DateGiven, userId);
            record.ExpenseId = expense.Id;
            await _context.SaveChangesAsync();
        }

        return Result<VaccinationRecordDto>.Success(new VaccinationRecordDto
        {
            Id = record.Id,
            FarmId = record.FarmId,
            AnimalId = record.AnimalId,
            AnimalTagNumber = animal.TagNumber,
            AnimalName = animal.Name,
            VaccineTypeId = record.VaccineTypeId,
            VaccineTypeName = vaxType.Name,
            DateGiven = record.DateGiven,
            VetName = record.VetName,
            BatchNumber = record.BatchNumber,
            Cost = record.Cost,
            Notes = record.Notes,
            CreatedAt = record.CreatedAt
        });
    }

    public async Task<Result<VaccinationRecordDto>> UpdateVaccinationRecordAsync(Guid farmId, Guid id, UpdateVaccinationRecordRequest request)
    {
        var record = await _context.VaccinationRecords
            .Include(vr => vr.Animal)
            .Include(vr => vr.VaccineType)
            .FirstOrDefaultAsync(vr => vr.FarmId == farmId && vr.Id == id);

        if (record == null)
            return Result<VaccinationRecordDto>.NotFound("Vaccination record not found");

        if (record.AnimalId != request.AnimalId)
        {
            var animal = await _context.Animals
                .FirstOrDefaultAsync(a => a.Id == request.AnimalId && a.FarmId == farmId);
            if (animal == null)
                return Result<VaccinationRecordDto>.Validation("Animal not found in this farm");
        }

        if (record.VaccineTypeId != request.VaccineTypeId)
        {
            var vaxType = await _context.VaccineTypes
                .FirstOrDefaultAsync(v => v.Id == request.VaccineTypeId && v.FarmId == farmId);
            if (vaxType == null)
                return Result<VaccinationRecordDto>.Validation("Vaccine type not found in this farm");
        }

        var userId = _currentUser.GetUserId();

        record.AnimalId = request.AnimalId;
        record.VaccineTypeId = request.VaccineTypeId;
        record.DateGiven = request.DateGiven;
        record.VetName = request.VetName?.Trim();
        record.BatchNumber = request.BatchNumber?.Trim();
        record.Cost = request.Cost;
        record.Notes = request.Notes?.Trim();
        record.ModifiedAt = DateTime.UtcNow;
        record.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        var updated = await _context.VaccinationRecords
            .AsNoTracking()
            .Where(vr => vr.Id == id)
            .Select(vr => new VaccinationRecordDto
            {
                Id = vr.Id,
                FarmId = vr.FarmId,
                AnimalId = vr.AnimalId,
                AnimalTagNumber = vr.Animal.TagNumber,
                AnimalName = vr.Animal.Name,
                VaccineTypeId = vr.VaccineTypeId,
                VaccineTypeName = vr.VaccineType.Name,
                DateGiven = vr.DateGiven,
                VetName = vr.VetName,
                BatchNumber = vr.BatchNumber,
                Cost = vr.Cost,
                Notes = vr.Notes,
                CreatedAt = vr.CreatedAt
            })
            .FirstAsync();

        return Result<VaccinationRecordDto>.Success(updated);
    }

    public async Task<Result> DeleteVaccinationRecordAsync(Guid farmId, Guid id)
    {
        var record = await _context.VaccinationRecords
            .FirstOrDefaultAsync(vr => vr.FarmId == farmId && vr.Id == id);

        if (record == null)
            return Result.NotFound("Vaccination record not found");

        _context.VaccinationRecords.Remove(record);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // ── VaccinationSchedule CRUD ──────────────────────────

    public async Task<Result<PagedResult<VaccinationScheduleDto>>> GetSchedulesAsync(Guid farmId, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 || pageSize > 100 ? 20 : pageSize;

        var query = _context.VaccinationSchedules
            .AsNoTracking()
            .Where(s => s.FarmId == farmId);

        var totalCount = await query.CountAsync();

        var schedules = await query
            .OrderBy(s => s.VaccineType.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new VaccinationScheduleDto
            {
                Id = s.Id,
                VaccineTypeId = s.VaccineTypeId,
                VaccineTypeName = s.VaccineType.Name,
                AnimalTypeId = s.AnimalTypeId,
                AnimalTypeName = s.AnimalType != null ? s.AnimalType.Name : null,
                BreedId = s.BreedId,
                BreedName = s.Breed != null ? s.Breed.Name : null,
                RecurrenceDays = s.RecurrenceDays,
                IsActive = s.IsActive,
                Notes = s.Notes,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync();

        return Result<PagedResult<VaccinationScheduleDto>>.Success(new PagedResult<VaccinationScheduleDto>
        {
            Items = schedules,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<VaccinationScheduleDto>> CreateScheduleAsync(Guid farmId, CreateVaccinationScheduleRequest request)
    {
        var vaxType = await _context.VaccineTypes
            .FirstOrDefaultAsync(v => v.Id == request.VaccineTypeId && v.FarmId == farmId);
        if (vaxType == null)
            return Result<VaccinationScheduleDto>.Validation("Vaccine type not found in this farm");

        if (request.RecurrenceDays <= 0)
            return Result<VaccinationScheduleDto>.Validation("Recurrence interval must be greater than zero");

        if (request.AnimalTypeId.HasValue)
        {
            var animalType = await _context.AnimalTypes
                .FirstOrDefaultAsync(a => a.Id == request.AnimalTypeId.Value && a.FarmId == farmId);
            if (animalType == null)
                return Result<VaccinationScheduleDto>.Validation("Animal type not found in this farm");
        }

        if (request.BreedId.HasValue)
        {
            var breed = await _context.Breeds
                .FirstOrDefaultAsync(b => b.Id == request.BreedId.Value);
            if (breed == null)
                return Result<VaccinationScheduleDto>.Validation("Breed not found");
        }

        var userId = _currentUser.GetUserId();

        var schedule = new VaccinationSchedule
        {
            FarmId = farmId,
            VaccineTypeId = request.VaccineTypeId,
            AnimalTypeId = request.AnimalTypeId,
            BreedId = request.BreedId,
            RecurrenceDays = request.RecurrenceDays,
            IsActive = request.IsActive,
            Notes = request.Notes?.Trim(),
            CreatedBy = userId
        };

        _context.VaccinationSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        return Result<VaccinationScheduleDto>.Success(new VaccinationScheduleDto
        {
            Id = schedule.Id,
            VaccineTypeId = schedule.VaccineTypeId,
            VaccineTypeName = vaxType.Name,
            AnimalTypeId = schedule.AnimalTypeId,
            AnimalTypeName = null,
            BreedId = schedule.BreedId,
            BreedName = null,
            RecurrenceDays = schedule.RecurrenceDays,
            IsActive = schedule.IsActive,
            Notes = schedule.Notes,
            CreatedAt = schedule.CreatedAt
        });
    }

    public async Task<Result<VaccinationScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateVaccinationScheduleRequest request)
    {
        var schedule = await _context.VaccinationSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.FarmId == farmId);

        if (schedule == null)
            return Result<VaccinationScheduleDto>.NotFound("Vaccination schedule not found");

        var vaxType = await _context.VaccineTypes
            .FirstOrDefaultAsync(v => v.Id == request.VaccineTypeId && v.FarmId == farmId);
        if (vaxType == null)
            return Result<VaccinationScheduleDto>.Validation("Vaccine type not found in this farm");

        if (request.RecurrenceDays <= 0)
            return Result<VaccinationScheduleDto>.Validation("Recurrence interval must be greater than zero");

        var userId = _currentUser.GetUserId();

        schedule.VaccineTypeId = request.VaccineTypeId;
        schedule.AnimalTypeId = request.AnimalTypeId;
        schedule.BreedId = request.BreedId;
        schedule.RecurrenceDays = request.RecurrenceDays;
        schedule.IsActive = request.IsActive;
        schedule.Notes = request.Notes?.Trim();
        schedule.ModifiedAt = DateTime.UtcNow;
        schedule.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        return Result<VaccinationScheduleDto>.Success(new VaccinationScheduleDto
        {
            Id = schedule.Id,
            VaccineTypeId = schedule.VaccineTypeId,
            VaccineTypeName = vaxType.Name,
            AnimalTypeId = schedule.AnimalTypeId,
            AnimalTypeName = null,
            BreedId = schedule.BreedId,
            BreedName = null,
            RecurrenceDays = schedule.RecurrenceDays,
            IsActive = schedule.IsActive,
            Notes = schedule.Notes,
            CreatedAt = schedule.CreatedAt
        });
    }

    public async Task<Result> DeleteScheduleAsync(Guid farmId, Guid id)
    {
        var schedule = await _context.VaccinationSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.FarmId == farmId);

        if (schedule == null)
            return Result.NotFound("Vaccination schedule not found");

        _context.VaccinationSchedules.Remove(schedule);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // ── Vaccination Status ────────────────────────────────

    public async Task<Result<List<VaccinationStatusDto>>> GetVaccinationStatusAsync(Guid farmId)
    {
        var statuses = await ComputeVaccinationStatusesAsync(farmId);
        return Result<List<VaccinationStatusDto>>.Success(statuses);
    }

    public async Task<Result<List<VaccinationStatusDto>>> GetOverdueVaccinationsAsync(Guid farmId)
    {
        var all = await ComputeVaccinationStatusesAsync(farmId);
        var overdue = all.Where(s => s.Status == VaccinationStatusType.Overdue || s.Status == VaccinationStatusType.Due).ToList();
        return Result<List<VaccinationStatusDto>>.Success(overdue);
    }

    private async Task<List<VaccinationStatusDto>> ComputeVaccinationStatusesAsync(Guid farmId)
    {
        var schedules = await _context.VaccinationSchedules
            .AsNoTracking()
            .Where(s => s.FarmId == farmId && s.IsActive)
            .Include(s => s.VaccineType)
            .ToListAsync();

        var animals = _context.Animals
            .AsNoTracking()
            .Where(a => a.FarmId == farmId);

        // Filter by animal type/breed if schedule specifies them
        var animalList = await animals.ToListAsync();

        var results = new List<VaccinationStatusDto>();
        var today = DateTime.UtcNow.Date;

        foreach (var schedule in schedules)
        {
            // Find matching animals
            var matchingAnimals = animalList.Where(a =>
                (!schedule.AnimalTypeId.HasValue || a.AnimalTypeId == schedule.AnimalTypeId) &&
                (!schedule.BreedId.HasValue || a.BreedId == schedule.BreedId)
            ).ToList();

            foreach (var animal in matchingAnimals)
            {
                // Get last vaccination for this animal + vaccine type
                var lastRecord = await _context.VaccinationRecords
                    .AsNoTracking()
                    .Where(vr => vr.AnimalId == animal.Id && vr.VaccineTypeId == schedule.VaccineTypeId)
                    .OrderByDescending(vr => vr.DateGiven)
                    .FirstOrDefaultAsync();

                var lastDate = lastRecord?.DateGiven;
                var nextDue = lastDate.HasValue
                    ? lastDate.Value.AddDays(schedule.RecurrenceDays)
                    : today; // Never vaccinated = due now

                var daysUntilDue = (nextDue.Date - today).Days;
                VaccinationStatusType status;
                if (daysUntilDue < 0)
                    status = VaccinationStatusType.Overdue;
                else if (daysUntilDue <= 7)
                    status = VaccinationStatusType.Due;
                else
                    status = VaccinationStatusType.Upcoming;

                results.Add(new VaccinationStatusDto
                {
                    AnimalId = animal.Id,
                    AnimalTagNumber = animal.TagNumber,
                    AnimalName = animal.Name,
                    VaccineTypeId = schedule.VaccineTypeId,
                    VaccineTypeName = schedule.VaccineType.Name,
                    LastVaccinationDate = lastDate,
                    NextDueDate = nextDue,
                    Status = status,
                    StatusName = status.ToString(),
                    DaysUntilDue = daysUntilDue
                });
            }
        }

        return results.OrderBy(s => s.DaysUntilDue).ToList();
    }

    private async Task<bool> DeductLinkedMedicineAsync(Guid farmId, Guid medicineId, Guid? userId, Guid vaccinationId, DateTime dateUsed)
    {
        var batches = await _context.MedicineStocks.Where(s => s.FarmId == farmId && s.MedicineId == medicineId && s.Quantity > 0).OrderBy(s => s.ExpiryDate).ToListAsync();
        if (batches.Count == 0) return false;
        var batch = batches[0];
        batch.Quantity--;
        _context.MedicineUsages.Add(new MedicineUsage
        {
            FarmId = farmId, MedicineId = medicineId, MedicineStockId = batch.Id,
            QuantityUsed = 1, DateUsed = dateUsed, Notes = $"Vaccination {vaccinationId}", CreatedBy = userId
        });
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<Expense> AutoCreateVetExpenseAsync(Guid farmId, decimal amount, string? vetName, string description, DateTime expenseDate, Guid? userId)
    {
        var category = await _context.ExpenseCategories
            .FirstOrDefaultAsync(c => c.FarmId == farmId && c.Name == "Veterinary");
        if (category == null)
        {
            category = new ExpenseCategory
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = "Veterinary",
                Description = "Auto-created for health records",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            _context.ExpenseCategories.Add(category);
        }

        var paymentMethod = await _context.PaymentMethods
            .FirstOrDefaultAsync(p => p.FarmId == farmId && p.Name == "Cash");
        if (paymentMethod == null)
        {
            paymentMethod = new PaymentMethod
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = "Cash",
                Description = "Default payment method",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            _context.PaymentMethods.Add(paymentMethod);
        }

        var expense = new Expense
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            ExpenseDate = expenseDate,
            Amount = Math.Round(amount, 2),
            ExpenseCategoryId = category.Id,
            PaymentMethodId = paymentMethod.Id,
            Description = $"{description} (Vet: {vetName ?? "N/A"})",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();

        return expense;
    }
}
