using FMS.Application.Breeding;
using FMS.Application.Common;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Breeding;

public class BreedingService : IBreedingService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public BreedingService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<BreedingRecordDto>>> GetBreedingRecordsAsync(Guid farmId, BreedingRecordListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : Math.Min(filter.PageSize, 200);

        var query = _context.BreedingRecords
            .AsNoTracking()
            .Where(br => br.FarmId == farmId);

        if (filter.SireId.HasValue)
            query = query.Where(br => br.SireId == filter.SireId.Value);

        if (filter.DamId.HasValue)
            query = query.Where(br => br.DamId == filter.DamId.Value);

        if (filter.Result.HasValue)
            query = query.Where(br => br.Result == filter.Result.Value);

        if (filter.FromDate.HasValue)
            query = query.Where(br => br.BreedingDate >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(br => br.BreedingDate <= filter.ToDate.Value);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(br => br.BreedingDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(br => new BreedingRecordDto
            {
                Id = br.Id,
                FarmId = br.FarmId,
                SireId = br.SireId,
                SireTagNumber = br.Sire.TagNumber,
                SireName = br.Sire.Name,
                DamId = br.DamId,
                DamTagNumber = br.Dam.TagNumber,
                DamName = br.Dam.Name,
                BreedingDate = br.BreedingDate,
                Method = br.Method,
                VetName = br.VetName,
                Result = br.Result,
                Notes = br.Notes,
                CreatedAt = br.CreatedAt
            })
            .ToListAsync();

        return Result<PagedResult<BreedingRecordDto>>.Success(new PagedResult<BreedingRecordDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    public async Task<Result<BreedingRecordDto>> GetBreedingRecordByIdAsync(Guid farmId, Guid id)
    {
        var record = await _context.BreedingRecords
            .AsNoTracking()
            .Include(br => br.Sire)
            .Include(br => br.Dam)
            .FirstOrDefaultAsync(br => br.Id == id && br.FarmId == farmId);

        if (record == null)
            return Result<BreedingRecordDto>.NotFound("Breeding record not found");

        return Result<BreedingRecordDto>.Success(MapToDto(record));
    }

    public async Task<Result<BreedingRecordDto>> CreateBreedingRecordAsync(Guid farmId, CreateBreedingRecordRequest request)
    {
        var sire = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == request.SireId && a.FarmId == farmId && !a.IsDeleted);
        if (sire == null)
            return Result<BreedingRecordDto>.NotFound("Sire animal not found");

        var dam = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == request.DamId && a.FarmId == farmId && !a.IsDeleted);
        if (dam == null)
            return Result<BreedingRecordDto>.NotFound("Dam animal not found");

        if (request.SireId == request.DamId)
            return Result<BreedingRecordDto>.Validation("Sire and dam must be different animals");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var record = new BreedingRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            SireId = request.SireId,
            DamId = request.DamId,
            BreedingDate = request.BreedingDate,
            Method = request.Method,
            VetName = request.VetName?.Trim(),
            Result = BreedingResult.Pending,
            Notes = request.Notes?.Trim(),
            CreatedAt = now,
            CreatedBy = userId
        };

        _context.BreedingRecords.Add(record);

        // Add timeline events for both sire and dam
        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = request.SireId,
            FarmId = farmId,
            EventType = TimelineEventTypes.BreedingRecord,
            Title = $"Breeding recorded ({request.Method})",
            Description = $"Breeding with {dam.TagNumber} on {request.BreedingDate:yyyy-MM-dd}",
            RelatedEntityId = record.Id,
            RelatedEntityType = "BreedingRecord",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = request.DamId,
            FarmId = farmId,
            EventType = TimelineEventTypes.BreedingRecord,
            Title = $"Breeding recorded ({request.Method})",
            Description = $"Breeding with {sire.TagNumber} on {request.BreedingDate:yyyy-MM-dd}",
            RelatedEntityId = record.Id,
            RelatedEntityType = "BreedingRecord",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        await _context.SaveChangesAsync();

        return await GetBreedingRecordByIdAsync(farmId, record.Id);
    }

    public async Task<Result<BreedingRecordDto>> UpdateBreedingRecordAsync(Guid farmId, Guid id, UpdateBreedingRecordRequest request)
    {
        var record = await _context.BreedingRecords
            .Include(br => br.Sire)
            .Include(br => br.Dam)
            .FirstOrDefaultAsync(br => br.Id == id && br.FarmId == farmId);

        if (record == null)
            return Result<BreedingRecordDto>.NotFound("Breeding record not found");

        var sire = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == request.SireId && a.FarmId == farmId && !a.IsDeleted);
        if (sire == null)
            return Result<BreedingRecordDto>.NotFound("Sire animal not found");

        var dam = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == request.DamId && a.FarmId == farmId && !a.IsDeleted);
        if (dam == null)
            return Result<BreedingRecordDto>.NotFound("Dam animal not found");

        if (request.SireId == request.DamId)
            return Result<BreedingRecordDto>.Validation("Sire and dam must be different animals");

        record.SireId = request.SireId;
        record.DamId = request.DamId;
        record.BreedingDate = request.BreedingDate;
        record.Method = request.Method;
        record.VetName = request.VetName?.Trim();
        record.Result = request.Result;
        record.Notes = request.Notes?.Trim();
        record.ModifiedAt = DateTime.UtcNow;
        record.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await GetBreedingRecordByIdAsync(farmId, id);
    }

    public async Task<Result> DeleteBreedingRecordAsync(Guid farmId, Guid id)
    {
        var record = await _context.BreedingRecords
            .FirstOrDefaultAsync(br => br.Id == id && br.FarmId == farmId);

        if (record == null)
            return Result.NotFound("Breeding record not found");

        _context.BreedingRecords.Remove(record);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Gestation Tracking

    public async Task<Result<PagedResult<GestationRecordDto>>> GetGestationRecordsAsync(Guid farmId, GestationRecordListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : Math.Min(filter.PageSize, 200);

        var query = _context.GestationRecords
            .AsNoTracking()
            .Where(gr => gr.FarmId == farmId);

        if (filter.ActiveOnly)
            query = query.Where(gr => gr.ConfirmedDate != null);

        if (filter.Stage.HasValue)
            query = query.Where(gr => gr.CurrentStage == filter.Stage.Value);

        var total = await query.CountAsync();

        var now = DateTime.UtcNow;
        var items = await query
            .OrderBy(gr => gr.ExpectedDeliveryDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(gr => new GestationRecordDto
            {
                Id = gr.Id,
                FarmId = gr.FarmId,
                BreedingRecordId = gr.BreedingRecordId,
                AnimalId = gr.AnimalId,
                AnimalTagNumber = gr.Animal.TagNumber,
                AnimalName = gr.Animal.Name,
                SireId = gr.BreedingRecord.SireId,
                SireTagNumber = gr.BreedingRecord.Sire.TagNumber,
                BreedingDate = gr.BreedingRecord.BreedingDate,
                ConfirmedDate = gr.ConfirmedDate,
                ExpectedDeliveryDate = gr.ExpectedDeliveryDate,
                DaysUntilDue = EF.Functions.DateDiffDay(now, gr.ExpectedDeliveryDate),
                DaysElapsed = EF.Functions.DateDiffDay(gr.BreedingRecord.BreedingDate, now),
                CurrentStage = gr.CurrentStage,
                HealthCheckNotes = gr.HealthCheckNotes,
                HealthCheckCount = gr.HealthChecks.Count,
                CreatedAt = gr.CreatedAt
            })
            .ToListAsync();

        return Result<PagedResult<GestationRecordDto>>.Success(new PagedResult<GestationRecordDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    public async Task<Result<GestationRecordDto>> GetGestationRecordByIdAsync(Guid farmId, Guid id)
    {
        var gr = await _context.GestationRecords
            .AsNoTracking()
            .Include(g => g.Animal)
            .Include(g => g.BreedingRecord).ThenInclude(br => br.Sire)
            .Include(g => g.HealthChecks)
            .FirstOrDefaultAsync(g => g.Id == id && g.FarmId == farmId);

        if (gr == null)
            return Result<GestationRecordDto>.NotFound("Gestation record not found");

        var now = DateTime.UtcNow;
        return Result<GestationRecordDto>.Success(new GestationRecordDto
        {
            Id = gr.Id,
            FarmId = gr.FarmId,
            BreedingRecordId = gr.BreedingRecordId,
            AnimalId = gr.AnimalId,
            AnimalTagNumber = gr.Animal.TagNumber,
            AnimalName = gr.Animal.Name,
            SireId = gr.BreedingRecord.SireId,
            SireTagNumber = gr.BreedingRecord.Sire.TagNumber,
            BreedingDate = gr.BreedingRecord.BreedingDate,
            ConfirmedDate = gr.ConfirmedDate,
            ExpectedDeliveryDate = gr.ExpectedDeliveryDate,
            DaysUntilDue = (gr.ExpectedDeliveryDate - now).Days,
            DaysElapsed = (now - gr.BreedingRecord.BreedingDate).Days,
            CurrentStage = gr.CurrentStage,
            HealthCheckNotes = gr.HealthCheckNotes,
            HealthCheckCount = gr.HealthChecks.Count,
            CreatedAt = gr.CreatedAt
        });
    }

    public async Task<Result<GestationRecordDto>> ConfirmPregnancyAsync(Guid farmId, ConfirmPregnancyRequest request)
    {
        var breedingRecord = await _context.BreedingRecords
            .Include(br => br.Sire)
            .Include(br => br.Dam).ThenInclude(d => d.AnimalStatus)
            .FirstOrDefaultAsync(br => br.Id == request.BreedingRecordId && br.FarmId == farmId);

        if (breedingRecord == null)
            return Result<GestationRecordDto>.NotFound("Breeding record not found");

        if (breedingRecord.Result == BreedingResult.Failed)
            return Result<GestationRecordDto>.Validation("Cannot confirm pregnancy for a failed breeding record");

        var existingGestation = await _context.GestationRecords
            .FirstOrDefaultAsync(gr => gr.BreedingRecordId == request.BreedingRecordId && gr.ConfirmedDate != null);

        if (existingGestation != null)
            return Result<GestationRecordDto>.Conflict("Pregnancy already confirmed for this breeding record");

        var breed = await _context.Breeds.FindAsync(breedingRecord.Dam.BreedId);
        var gestationDays = breed?.AverageGestationDays ?? 283;
        var expectedDelivery = breedingRecord.BreedingDate.AddDays(gestationDays);

        var stage = CalculateStage(breedingRecord.BreedingDate, expectedDelivery);

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var gestationRecord = new GestationRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            BreedingRecordId = request.BreedingRecordId,
            AnimalId = breedingRecord.DamId,
            ConfirmedDate = request.ConfirmedDate,
            ExpectedDeliveryDate = expectedDelivery,
            CurrentStage = stage,
            CreatedAt = now,
            CreatedBy = userId
        };

        _context.GestationRecords.Add(gestationRecord);

        // Update breeding record result to Confirmed
        breedingRecord.Result = BreedingResult.Confirmed;
        breedingRecord.ModifiedAt = now;
        breedingRecord.ModifiedBy = userId;

        // Update dam status to Pregnant
        var pregnantStatus = await _context.AnimalStatuses
            .FirstOrDefaultAsync(s => s.FarmId == farmId && s.IsSystemDefined && s.Name == "Pregnant");

        if (pregnantStatus != null)
        {
            var previousStatusId = breedingRecord.Dam.AnimalStatusId;
            breedingRecord.Dam.AnimalStatusId = pregnantStatus.Id;
            breedingRecord.Dam.ModifiedAt = now;
            breedingRecord.Dam.ModifiedBy = userId;

            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = breedingRecord.DamId,
                FarmId = farmId,
                EventType = TimelineEventTypes.StatusChanged,
                Title = "Status changed to Pregnant",
                Description = $"Pregnancy confirmed. Expected delivery: {expectedDelivery:yyyy-MM-dd}",
                RelatedEntityId = gestationRecord.Id,
                RelatedEntityType = "GestationRecord",
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = userId
            });
        }

        await _context.SaveChangesAsync();

        return await GetGestationRecordByIdAsync(farmId, gestationRecord.Id);
    }

    public async Task<Result> RevertPregnancyAsync(Guid farmId, Guid gestationRecordId, string reason)
    {
        var gestation = await _context.GestationRecords
            .Include(g => g.Animal)
            .Include(g => g.BreedingRecord)
            .FirstOrDefaultAsync(g => g.Id == gestationRecordId && g.FarmId == farmId);

        if (gestation == null)
            return Result.NotFound("Gestation record not found");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        // Update breeding record result to Failed
        var breedingRecord = await _context.BreedingRecords
            .FirstOrDefaultAsync(br => br.Id == gestation.BreedingRecordId);

        if (breedingRecord != null)
        {
            breedingRecord.Result = BreedingResult.Failed;
            breedingRecord.ModifiedAt = now;
            breedingRecord.ModifiedBy = userId;
        }

        // Revert dam status to Active (find the system-defined Active status)
        var activeStatus = await _context.AnimalStatuses
            .FirstOrDefaultAsync(s => s.FarmId == farmId && s.IsSystemDefined && s.Name == "Active");

        if (activeStatus != null)
        {
            gestation.Animal.AnimalStatusId = activeStatus.Id;
            gestation.Animal.ModifiedAt = now;
            gestation.Animal.ModifiedBy = userId;

            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = gestation.AnimalId,
                FarmId = farmId,
                EventType = TimelineEventTypes.StatusChanged,
                Title = "Status reverted to Active",
                Description = $"Pregnancy failed/reverted. Reason: {reason}",
                RelatedEntityId = gestationRecordId,
                RelatedEntityType = "GestationRecord",
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = userId
            });
        }

        // Remove the gestation record
        _context.GestationRecords.Remove(gestation);

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<List<GestationHealthCheckDto>>> GetHealthChecksAsync(Guid farmId, Guid gestationRecordId)
    {
        var gestation = await _context.GestationRecords
            .FirstOrDefaultAsync(g => g.Id == gestationRecordId && g.FarmId == farmId);

        if (gestation == null)
            return Result<List<GestationHealthCheckDto>>.NotFound("Gestation record not found");

        var checks = await _context.GestationHealthChecks
            .AsNoTracking()
            .Where(ghc => ghc.GestationRecordId == gestationRecordId)
            .OrderByDescending(ghc => ghc.CheckDate)
            .Select(ghc => new GestationHealthCheckDto
            {
                Id = ghc.Id,
                GestationRecordId = ghc.GestationRecordId,
                CheckDate = ghc.CheckDate,
                Notes = ghc.Notes,
                PerformedBy = ghc.PerformedBy,
                WeightKg = ghc.WeightKg,
                CreatedAt = ghc.CreatedAt
            })
            .ToListAsync();

        return Result<List<GestationHealthCheckDto>>.Success(checks);
    }

    public async Task<Result<GestationHealthCheckDto>> LogHealthCheckAsync(Guid farmId, Guid gestationRecordId, LogGestationHealthCheckRequest request)
    {
        var gestation = await _context.GestationRecords
            .FirstOrDefaultAsync(g => g.Id == gestationRecordId && g.FarmId == farmId);

        if (gestation == null)
            return Result<GestationHealthCheckDto>.NotFound("Gestation record not found");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var healthCheck = new GestationHealthCheck
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            GestationRecordId = gestationRecordId,
            CheckDate = request.CheckDate,
            Notes = request.Notes?.Trim(),
            PerformedBy = request.PerformedBy?.Trim(),
            WeightKg = request.WeightKg,
            CreatedAt = now,
            CreatedBy = userId
        };

        _context.GestationHealthChecks.Add(healthCheck);

        // Update gestation record health check notes
        gestation.HealthCheckNotes = request.Notes?.Trim();
        gestation.ModifiedAt = now;
        gestation.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        return Result<GestationHealthCheckDto>.Success(new GestationHealthCheckDto
        {
            Id = healthCheck.Id,
            GestationRecordId = healthCheck.GestationRecordId,
            CheckDate = healthCheck.CheckDate,
            Notes = healthCheck.Notes,
            PerformedBy = healthCheck.PerformedBy,
            WeightKg = healthCheck.WeightKg,
            CreatedAt = healthCheck.CreatedAt
        });
    }

    private static GestationStage CalculateStage(DateTime breedingDate, DateTime expectedDelivery)
    {
        var totalDays = (expectedDelivery - breedingDate).Days;
        var elapsed = (DateTime.UtcNow - breedingDate).Days;
        var progress = (double)elapsed / totalDays;

        if (progress > 1.0) return GestationStage.Overdue;
        if (progress > 0.75) return GestationStage.Late;
        if (progress > 0.4) return GestationStage.Mid;
        return GestationStage.Early;
    }

    private static BreedingRecordDto MapToDto(BreedingRecord record)
    {
        return new BreedingRecordDto
        {
            Id = record.Id,
            FarmId = record.FarmId,
            SireId = record.SireId,
            SireTagNumber = record.Sire.TagNumber,
            SireName = record.Sire.Name,
            DamId = record.DamId,
            DamTagNumber = record.Dam.TagNumber,
            DamName = record.Dam.Name,
            BreedingDate = record.BreedingDate,
            Method = record.Method,
            VetName = record.VetName,
            Result = record.Result,
            Notes = record.Notes,
            CreatedAt = record.CreatedAt
        };
    }

    // ─── Birth Recording ───────────────────────────────────────────

    public async Task<Result<PagedResult<BirthRecordDto>>> GetBirthRecordsAsync(Guid farmId, BirthRecordListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : Math.Min(filter.PageSize, 200);

        var query = _context.BirthRecords
            .AsNoTracking()
            .Where(br => br.FarmId == farmId);

        if (filter.DamId.HasValue)
            query = query.Where(br => br.DamId == filter.DamId.Value);

        if (filter.FromDate.HasValue)
            query = query.Where(br => br.BirthDate >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(br => br.BirthDate <= filter.ToDate.Value);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(br => br.BirthDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(br => br.Dam)
            .Include(br => br.Offspring)
                .ThenInclude(bo => bo.SexOption)
            .Select(br => new BirthRecordDto
            {
                Id = br.Id,
                FarmId = br.FarmId,
                DamId = br.DamId,
                DamTagNumber = br.Dam.TagNumber,
                DamName = br.Dam.Name,
                GestationRecordId = br.GestationRecordId,
                BreedingRecordId = br.BreedingRecordId,
                BirthDate = br.BirthDate,
                OffspringCount = br.OffspringCount,
                AliveCount = br.Offspring.Count(o => o.Outcome == BirthOutcome.Alive),
                VetName = br.VetName,
                Notes = br.Notes,
                CreatedAt = br.CreatedAt,
                Offspring = br.Offspring.Select(o => new BirthOffspringDto
                {
                    Id = o.Id,
                    TagNumber = o.TagNumber,
                    Name = o.Name,
                    SexOptionId = o.SexOptionId,
                    SexValue = o.SexOption.Value,
                    Outcome = o.Outcome,
                    BirthWeightKg = o.BirthWeightKg,
                    OffspringAnimalId = o.OffspringAnimalId,
                    Notes = o.Notes
                }).ToList()
            })
            .ToListAsync();

        return Result<PagedResult<BirthRecordDto>>.Success(new PagedResult<BirthRecordDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    public async Task<Result<BirthRecordDto>> GetBirthRecordByIdAsync(Guid farmId, Guid id)
    {
        var record = await _context.BirthRecords
            .AsNoTracking()
            .Include(br => br.Dam)
            .Include(br => br.Offspring)
                .ThenInclude(bo => bo.SexOption)
            .FirstOrDefaultAsync(br => br.Id == id && br.FarmId == farmId);

        if (record == null)
            return Result<BirthRecordDto>.NotFound("Birth record not found");

        return Result<BirthRecordDto>.Success(MapBirthToDto(record));
    }

    public async Task<Result<BirthRecordDto>> CreateBirthRecordAsync(Guid farmId, CreateBirthRecordRequest request)
    {
        if (request.Offspring == null || request.Offspring.Count == 0)
            return Result<BirthRecordDto>.Validation("At least one offspring is required");

        // Validate dam
        var dam = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == request.DamId && a.FarmId == farmId && !a.IsDeleted);
        if (dam == null)
            return Result<BirthRecordDto>.NotFound("Dam animal not found");

        // Validate optional gestation record
        GestationRecord? gestationRecord = null;
        if (request.GestationRecordId.HasValue)
        {
            gestationRecord = await _context.GestationRecords
                .FirstOrDefaultAsync(g => g.Id == request.GestationRecordId.Value && g.FarmId == farmId && g.AnimalId == request.DamId);
            if (gestationRecord == null)
                return Result<BirthRecordDto>.NotFound("Gestation record not found");
        }

        // Validate optional breeding record
        if (request.BreedingRecordId.HasValue)
        {
            var breedingExists = await _context.BreedingRecords
                .AnyAsync(br => br.Id == request.BreedingRecordId.Value && br.FarmId == farmId && br.DamId == request.DamId);
            if (!breedingExists)
                return Result<BirthRecordDto>.NotFound("Breeding record not found");
        }

        // Validate sex options exist
        var sexIds = request.Offspring.Select(o => o.SexOptionId).Distinct().ToList();
        var sexOptions = await _context.SexOptions
            .Where(so => sexIds.Contains(so.Id) && so.FarmId == farmId)
            .ToListAsync();
        if (sexOptions.Count != sexIds.Count)
            return Result<BirthRecordDto>.Validation("One or more sex options are invalid");

        // Get Active status for offspring
        var activeStatus = await _context.AnimalStatuses
            .FirstOrDefaultAsync(s => s.FarmId == farmId && s.IsSystemDefined && s.Name == "Active");

        // Get next tag sequence for dam: count all previous offspring across all birth records for this dam
        var existingOffspringCount = await (
            from bo in _context.BirthOffspring
            join br in _context.BirthRecords on bo.BirthRecordId equals br.Id
            where br.FarmId == farmId && br.DamId == request.DamId
            select bo.Id).CountAsync();

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var birthRecord = new BirthRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            DamId = request.DamId,
            GestationRecordId = request.GestationRecordId,
            BreedingRecordId = request.BreedingRecordId,
            BirthDate = request.BirthDate,
            OffspringCount = request.Offspring.Count,
            VetName = request.VetName?.Trim(),
            Notes = request.Notes?.Trim(),
            CreatedAt = now,
            CreatedBy = userId
        };

        var offspringDtos = new List<BirthOffspringDto>();

        for (int i = 0; i < request.Offspring.Count; i++)
        {
            var offReq = request.Offspring[i];
            var tagNumber = $"{dam.TagNumber}-{existingOffspringCount + i + 1}";

            var offspringAnimal = new Domain.Entities.Animal
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                TagNumber = tagNumber,
                Name = offReq.Name?.Trim(),
                AnimalTypeId = dam.AnimalTypeId,
                BreedId = dam.BreedId,
                SexOptionId = offReq.SexOptionId,
                AnimalStatusId = activeStatus?.Id ?? dam.AnimalStatusId,
                SireId = dam.DamId != null ? null : null, // Sire comes from breeding/gestation record if available
                DamId = request.DamId,
                DateOfBirth = request.BirthDate,
                AcquisitionDate = request.BirthDate,
                Notes = offReq.Notes?.Trim(),
                CreatedAt = now,
                CreatedBy = userId
            };

            // Try to link sire from breeding record
            if (request.BreedingRecordId.HasValue)
            {
                var breedingRecord = await _context.BreedingRecords
                    .FirstOrDefaultAsync(br => br.Id == request.BreedingRecordId.Value);
                if (breedingRecord != null)
                    offspringAnimal.SireId = breedingRecord.SireId;
            }
            else if (gestationRecord != null)
            {
                var breedingRecord = await _context.BreedingRecords
                    .FirstOrDefaultAsync(br => br.Id == gestationRecord.BreedingRecordId);
                if (breedingRecord != null)
                    offspringAnimal.SireId = breedingRecord.SireId;
            }

            _context.Animals.Add(offspringAnimal);

            var birthOffspring = new BirthOffspring
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                BirthRecordId = birthRecord.Id,
                OffspringAnimalId = offspringAnimal.Id,
                TagNumber = tagNumber,
                Name = offReq.Name?.Trim(),
                SexOptionId = offReq.SexOptionId,
                Outcome = offReq.Outcome,
                BirthWeightKg = offReq.BirthWeightKg,
                Notes = offReq.Notes?.Trim(),
                CreatedAt = now
            };

            birthRecord.Offspring.Add(birthOffspring);

            offspringDtos.Add(new BirthOffspringDto
            {
                Id = birthOffspring.Id,
                TagNumber = tagNumber,
                Name = offReq.Name?.Trim(),
                SexOptionId = offReq.SexOptionId,
                SexValue = sexOptions.First(s => s.Id == offReq.SexOptionId).Value,
                Outcome = offReq.Outcome,
                BirthWeightKg = offReq.BirthWeightKg,
                OffspringAnimalId = offspringAnimal.Id,
                Notes = offReq.Notes?.Trim()
            });

            // Timeline event for each alive offspring
            if (offReq.Outcome == BirthOutcome.Alive)
            {
                _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
                {
                    Id = Guid.NewGuid(),
                    AnimalId = offspringAnimal.Id,
                    FarmId = farmId,
                    EventType = TimelineEventTypes.Created,
                    Title = "Born",
                    Description = $"Born to {dam.TagNumber} on {request.BirthDate:yyyy-MM-dd}. Tag: {tagNumber}",
                    RelatedEntityId = birthRecord.Id,
                    RelatedEntityType = "BirthRecord",
                    OccurredAt = now,
                    CreatedAt = now,
                    CreatedBy = userId
                });
            }
        }

        // Timeline event for dam
        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = request.DamId,
            FarmId = farmId,
            EventType = TimelineEventTypes.BirthRecord,
            Title = $"Birth recorded - {request.Offspring.Count} offspring",
            Description = $"Born on {request.BirthDate:yyyy-MM-dd}. Alive: {offspringDtos.Count(o => o.Outcome == BirthOutcome.Alive)}",
            RelatedEntityId = birthRecord.Id,
            RelatedEntityType = "BirthRecord",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        // If linked to a gestation record, update breeding record result to Confirmed
        if (gestationRecord != null)
        {
            var breedingRecord = await _context.BreedingRecords
                .FirstOrDefaultAsync(br => br.Id == gestationRecord.BreedingRecordId);
            if (breedingRecord != null && breedingRecord.Result == BreedingResult.Pending)
            {
                breedingRecord.Result = BreedingResult.Confirmed;
                breedingRecord.ModifiedAt = now;
                breedingRecord.ModifiedBy = userId;
            }
        }

        _context.BirthRecords.Add(birthRecord);
        await _context.SaveChangesAsync();

        return Result<BirthRecordDto>.Success(new BirthRecordDto
        {
            Id = birthRecord.Id,
            FarmId = birthRecord.FarmId,
            DamId = birthRecord.DamId,
            DamTagNumber = dam.TagNumber,
            DamName = dam.Name,
            GestationRecordId = birthRecord.GestationRecordId,
            BreedingRecordId = birthRecord.BreedingRecordId,
            BirthDate = birthRecord.BirthDate,
            OffspringCount = birthRecord.OffspringCount,
            AliveCount = offspringDtos.Count(o => o.Outcome == BirthOutcome.Alive),
            VetName = birthRecord.VetName,
            Notes = birthRecord.Notes,
            CreatedAt = birthRecord.CreatedAt,
            Offspring = offspringDtos
        });
    }

    public async Task<Result> DeleteBirthRecordAsync(Guid farmId, Guid id)
    {
        var record = await _context.BirthRecords
            .Include(br => br.Offspring)
            .FirstOrDefaultAsync(br => br.Id == id && br.FarmId == farmId);

        if (record == null)
            return Result.NotFound("Birth record not found");

        _context.BirthRecords.Remove(record);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    private static BirthRecordDto MapBirthToDto(BirthRecord record)
    {
        return new BirthRecordDto
        {
            Id = record.Id,
            FarmId = record.FarmId,
            DamId = record.DamId,
            DamTagNumber = record.Dam.TagNumber,
            DamName = record.Dam.Name,
            GestationRecordId = record.GestationRecordId,
            BreedingRecordId = record.BreedingRecordId,
            BirthDate = record.BirthDate,
            OffspringCount = record.OffspringCount,
            AliveCount = record.Offspring.Count(o => o.Outcome == BirthOutcome.Alive),
            VetName = record.VetName,
            Notes = record.Notes,
            CreatedAt = record.CreatedAt,
            Offspring = record.Offspring.Select(o => new BirthOffspringDto
            {
                Id = o.Id,
                TagNumber = o.TagNumber,
                Name = o.Name,
                SexOptionId = o.SexOptionId,
                SexValue = o.SexOption.Value,
                Outcome = o.Outcome,
                BirthWeightKg = o.BirthWeightKg,
                OffspringAnimalId = o.OffspringAnimalId,
                Notes = o.Notes
            }).ToList()
        };
    }

    // ─── Lineage ─────────────────────────────────────────────────

    public async Task<Result<LineageResponse>> GetLineageAsync(Guid farmId, Guid animalId, int ancestorDepth, int descendantDepth)
    {
        var root = await _context.Animals
            .AsNoTracking()
            .Include(a => a.Sire)
            .Include(a => a.Dam)
            .Include(a => a.Breed)
            .Include(a => a.SexOption)
            .Include(a => a.AnimalStatus)
            .FirstOrDefaultAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);

        if (root == null)
            return Result<LineageResponse>.NotFound("Animal not found");

        // Build ancestor map: childId -> { sireId, damId }
        var parentMap = new Dictionary<Guid, (Guid? SireId, Guid? DamId)>();
        var animalIdsToLoad = new HashSet<Guid> { animalId };
        var loadedIds = new HashSet<Guid>();

        // Load ancestors iteratively
        for (int depth = 0; depth < ancestorDepth; depth++)
        {
            var idsToQuery = animalIdsToLoad.Except(loadedIds).ToList();
            if (idsToQuery.Count == 0) break;

            var animals = await _context.Animals
                .AsNoTracking()
                .Where(a => idsToQuery.Contains(a.Id))
                .Select(a => new { a.Id, a.SireId, a.DamId })
                .ToListAsync();

            var nextIds = new HashSet<Guid>();
            foreach (var a in animals)
            {
                parentMap[a.Id] = (a.SireId, a.DamId);
                loadedIds.Add(a.Id);
                if (a.SireId.HasValue) nextIds.Add(a.SireId.Value);
                if (a.DamId.HasValue) nextIds.Add(a.DamId.Value);
            }
            animalIdsToLoad = nextIds;
        }

        // Collect all ancestor animal IDs to load details
        var allAncestorIds = parentMap.Values
            .SelectMany(v => new[] { v.SireId, v.DamId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var ancestorDetails = new Dictionary<Guid, Domain.Entities.Animal>();
        if (allAncestorIds.Count > 0)
        {
            var ancestors = await _context.Animals
                .AsNoTracking()
                .Where(a => allAncestorIds.Contains(a.Id))
                .Include(a => a.Breed)
                .Include(a => a.SexOption)
                .Include(a => a.AnimalStatus)
                .ToListAsync();

            foreach (var a in ancestors)
                ancestorDetails[a.Id] = a;
        }

        // Load descendants iteratively
        var offspringMap = new Dictionary<Guid, List<Guid>>();
        var descendantIdsToLoad = new HashSet<Guid> { animalId };
        var loadedDescendantIds = new HashSet<Guid>();

        for (int depth = 0; depth < descendantDepth; depth++)
        {
            var idsToQuery = descendantIdsToLoad.Except(loadedDescendantIds).ToList();
            if (idsToQuery.Count == 0) break;

            var children = await _context.Animals
                .AsNoTracking()
                .Where(a => (a.SireId.HasValue && idsToQuery.Contains(a.SireId.Value)) ||
                            (a.DamId.HasValue && idsToQuery.Contains(a.DamId.Value)))
                .Select(a => new { a.Id, a.SireId, a.DamId })
                .ToListAsync();

            var nextIds = new HashSet<Guid>();
            foreach (var child in children)
            {
                loadedDescendantIds.Add(child.Id);
                nextIds.Add(child.Id);

                if (child.SireId.HasValue && idsToQuery.Contains(child.SireId.Value))
                {
                    if (!offspringMap.ContainsKey(child.SireId.Value))
                        offspringMap[child.SireId.Value] = new List<Guid>();
                    offspringMap[child.SireId.Value].Add(child.Id);
                }
                if (child.DamId.HasValue && idsToQuery.Contains(child.DamId.Value))
                {
                    if (!offspringMap.ContainsKey(child.DamId.Value))
                        offspringMap[child.DamId.Value] = new List<Guid>();
                    offspringMap[child.DamId.Value].Add(child.Id);
                }
            }
            descendantIdsToLoad = nextIds;
        }

        // Load descendant animal details
        var allDescendantIds = offspringMap.Values.SelectMany(v => v).Distinct().ToList();
        var descendantDetails = new Dictionary<Guid, Domain.Entities.Animal>();
        if (allDescendantIds.Count > 0)
        {
            var descendants = await _context.Animals
                .AsNoTracking()
                .Where(a => allDescendantIds.Contains(a.Id))
                .Include(a => a.Breed)
                .Include(a => a.SexOption)
                .Include(a => a.AnimalStatus)
                .ToListAsync();

            foreach (var a in descendants)
                descendantDetails[a.Id] = a;
        }

        // Also load root's offspring
        var rootChildren = await _context.Animals
            .AsNoTracking()
            .Where(a => (a.SireId == animalId || a.DamId == animalId) && !a.IsDeleted)
            .Select(a => a.Id)
            .ToListAsync();

        if (!offspringMap.ContainsKey(animalId))
            offspringMap[animalId] = new List<Guid>();
        offspringMap[animalId] = rootChildren;

        // Load root children details if not already loaded
        var missingChildIds = rootChildren.Except(allDescendantIds).Except(new[] { animalId }).ToList();
        if (missingChildIds.Count > 0)
        {
            var missingChildren = await _context.Animals
                .AsNoTracking()
                .Where(a => missingChildIds.Contains(a.Id))
                .Include(a => a.Breed)
                .Include(a => a.SexOption)
                .Include(a => a.AnimalStatus)
                .ToListAsync();

            foreach (var a in missingChildren)
                descendantDetails[a.Id] = a;
        }

        // Build tree
        var visited = new HashSet<Guid>();
        var rootDto = BuildLineageNode(root, parentMap, offspringMap, ancestorDetails, descendantDetails, visited, isRoot: true);

        return Result<LineageResponse>.Success(new LineageResponse
        {
            Root = rootDto,
            AncestorDepth = ancestorDepth,
            DescendantDepth = descendantDepth
        });
    }

    private static LineageNodeDto BuildLineageNode(
        Domain.Entities.Animal animal,
        Dictionary<Guid, (Guid? SireId, Guid? DamId)> parentMap,
        Dictionary<Guid, List<Guid>> offspringMap,
        Dictionary<Guid, Domain.Entities.Animal> ancestorDetails,
        Dictionary<Guid, Domain.Entities.Animal> descendantDetails,
        HashSet<Guid> visited,
        bool isRoot = false)
    {
        var node = new LineageNodeDto
        {
            Id = animal.Id,
            TagNumber = animal.TagNumber,
            Name = animal.Name,
            Sex = animal.SexOption?.Value ?? "Unknown",
            Breed = animal.Breed?.Name,
            Status = animal.AnimalStatus?.Name ?? "Unknown",
            DateOfBirth = animal.DateOfBirth,
            IsRoot = isRoot
        };

        visited.Add(animal.Id);

        // Build sire (ancestor)
        if (parentMap.TryGetValue(animal.Id, out var parents) && parents.SireId.HasValue && !visited.Contains(parents.SireId.Value))
        {
            if (ancestorDetails.TryGetValue(parents.SireId.Value, out var sireAnimal))
                node.Sire = BuildLineageNode(sireAnimal, parentMap, offspringMap, ancestorDetails, descendantDetails, visited);
        }

        // Build dam (ancestor)
        if (parents.DamId.HasValue && !visited.Contains(parents.DamId.Value))
        {
            if (ancestorDetails.TryGetValue(parents.DamId.Value, out var damAnimal))
                node.Dam = BuildLineageNode(damAnimal, parentMap, offspringMap, ancestorDetails, descendantDetails, visited);
        }

        // Build offspring (descendants)
        if (offspringMap.TryGetValue(animal.Id, out var childIds))
        {
            foreach (var childId in childIds.Where(id => !visited.Contains(id)))
            {
                Domain.Entities.Animal? childAnimal = null;
                if (descendantDetails.TryGetValue(childId, out var found))
                    childAnimal = found;

                if (childAnimal != null)
                    node.Offspring.Add(BuildLineageNode(childAnimal, parentMap, offspringMap, ancestorDetails, descendantDetails, visited));
            }
        }

        return node;
    }

    // ─── Reports ─────────────────────────────────────────────────

    public async Task<Result<BreedingSummaryReport>> GetBreedingSummaryAsync(Guid farmId, BreedingReportFilter filter)
    {
        var query = _context.BreedingRecords.Where(br => br.FarmId == farmId);
        if (filter.FromDate.HasValue) query = query.Where(br => br.BreedingDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(br => br.BreedingDate <= filter.ToDate.Value);

        var total = await query.CountAsync();
        var pending = await query.CountAsync(br => br.Result == BreedingResult.Pending);
        var confirmed = await query.CountAsync(br => br.Result == BreedingResult.Confirmed);
        var failed = await query.CountAsync(br => br.Result == BreedingResult.Failed);
        var confirmationRate = total > 0 ? (double)confirmed / total * 100 : 0;

        var activePregnancies = await _context.GestationRecords
            .CountAsync(g => g.FarmId == farmId);

        var birthQuery = _context.BirthRecords.Where(br => br.FarmId == farmId);
        if (filter.FromDate.HasValue) birthQuery = birthQuery.Where(br => br.BirthDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) birthQuery = birthQuery.Where(br => br.BirthDate <= filter.ToDate.Value);

        var totalBirths = await birthQuery.CountAsync();
        var totalOffspring = await _context.BirthOffspring
            .CountAsync(bo => bo.FarmId == farmId);
        var aliveOffspring = await _context.BirthOffspring
            .CountAsync(bo => bo.FarmId == farmId && bo.Outcome == Domain.Enums.BirthOutcome.Alive);

        var avgGestation = await _context.GestationRecords
            .Where(g => g.FarmId == farmId && g.ConfirmedDate.HasValue)
            .AverageAsync(g => (double?)EF.Functions.DateDiffDay(
                g.BreedingRecord.BreedingDate,
                g.ConfirmedDate)) ?? 283.0;

        return Result<BreedingSummaryReport>.Success(new BreedingSummaryReport
        {
            TotalBreedingRecords = total,
            PendingCount = pending,
            ConfirmedCount = confirmed,
            FailedCount = failed,
            ConfirmationRate = Math.Round(confirmationRate, 1),
            ActivePregnancies = activePregnancies,
            TotalBirths = totalBirths,
            TotalOffspring = totalOffspring,
            AliveOffspring = aliveOffspring,
            AverageGestationDays = Math.Round(avgGestation, 0)
        });
    }

    public async Task<Result<List<BreedingTrendEntry>>> GetBreedingTrendAsync(Guid farmId, BreedingReportFilter filter)
    {
        var fromDate = filter.FromDate ?? DateTime.UtcNow.AddMonths(-12);
        var toDate = filter.ToDate ?? DateTime.UtcNow;

        var records = await _context.BreedingRecords
            .AsNoTracking()
            .Where(br => br.FarmId == farmId && br.BreedingDate >= fromDate && br.BreedingDate <= toDate)
            .ToListAsync();

        var trend = records
            .GroupBy(br => new { br.BreedingDate.Year, br.BreedingDate.Month })
            .Select(g => new BreedingTrendEntry
            {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                Count = g.Count(),
                Confirmed = g.Count(br => br.Result == BreedingResult.Confirmed),
                Failed = g.Count(br => br.Result == BreedingResult.Failed)
            })
            .OrderBy(t => t.Month)
            .ToList();

        return Result<List<BreedingTrendEntry>>.Success(trend);
    }

    public async Task<Result<List<MethodDistributionEntry>>> GetMethodDistributionAsync(Guid farmId, BreedingReportFilter filter)
    {
        var query = _context.BreedingRecords.Where(br => br.FarmId == farmId);
        if (filter.FromDate.HasValue) query = query.Where(br => br.BreedingDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(br => br.BreedingDate <= filter.ToDate.Value);

        var total = await query.CountAsync();
        if (total == 0)
            return Result<List<MethodDistributionEntry>>.Success(new List<MethodDistributionEntry>());

        var distribution = await query
            .GroupBy(br => br.Method)
            .Select(g => new MethodDistributionEntry
            {
                Method = g.Key.ToString()!,
                Count = g.Count(),
                Percentage = Math.Round((double)g.Count() / total * 100, 1)
            })
            .ToListAsync();

        return Result<List<MethodDistributionEntry>>.Success(distribution);
    }

    public async Task<Result<List<SirePerformanceEntry>>> GetSirePerformanceAsync(Guid farmId, BreedingReportFilter filter)
    {
        var query = _context.BreedingRecords
            .AsNoTracking()
            .Include(br => br.Sire)
            .Where(br => br.FarmId == farmId);
        if (filter.FromDate.HasValue) query = query.Where(br => br.BreedingDate >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(br => br.BreedingDate <= filter.ToDate.Value);

        var records = await query.ToListAsync();

        var sireGroups = records
            .GroupBy(br => br.SireId)
            .Select(g =>
            {
                var sire = g.First().Sire;
                var total = g.Count();
                var confirmed = g.Count(br => br.Result == BreedingResult.Confirmed);
                var failed = g.Count(br => br.Result == BreedingResult.Failed);
                var pending = g.Count(br => br.Result == BreedingResult.Pending);

                return new SirePerformanceEntry
                {
                    SireId = g.Key,
                    SireTagNumber = sire.TagNumber,
                    SireName = sire.Name,
                    TotalBreedings = total,
                    Confirmed = confirmed,
                    Failed = failed,
                    Pending = pending,
                    SuccessRate = total > 0 ? Math.Round((double)confirmed / total * 100, 1) : 0,
                    TotalOffspring = _context.Animals.Count(a => a.SireId == g.Key && !a.IsDeleted)
                };
            })
            .OrderByDescending(s => s.TotalBreedings)
            .ToList();

        return Result<List<SirePerformanceEntry>>.Success(sireGroups);
    }

    public async Task<Result<List<CalendarEventEntry>>> GetUpcomingCalendarEventsAsync(Guid farmId, int daysAhead)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(daysAhead);
        var events = new List<CalendarEventEntry>();

        // Upcoming expected births
        var upcomingBirths = await _context.GestationRecords
            .AsNoTracking()
            .Include(g => g.Animal)
            .Where(g => g.FarmId == farmId &&
                        g.ExpectedDeliveryDate >= now &&
                        g.ExpectedDeliveryDate <= cutoff)
            .OrderBy(g => g.ExpectedDeliveryDate)
            .ToListAsync();

        foreach (var gest in upcomingBirths)
        {
            var daysUntil = (gest.ExpectedDeliveryDate - now).Days;
            events.Add(new CalendarEventEntry
            {
                Id = gest.Id,
                EventType = "Expected Birth",
                AnimalTag = gest.Animal.TagNumber,
                AnimalName = gest.Animal.Name,
                EventDate = gest.ExpectedDeliveryDate,
                Details = $"Due in {daysUntil} day(s). Stage: {gest.CurrentStage}",
                Priority = daysUntil <= 7 ? "High" : daysUntil <= 14 ? "Medium" : "Normal"
            });
        }

        // Upcoming heat detection (estimated 21-day cycle for animals with failed/missing pregnancies)
        var recentFailedDams = await _context.BreedingRecords
            .AsNoTracking()
            .Include(br => br.Dam)
            .Where(br => br.FarmId == farmId &&
                         br.Result == BreedingResult.Failed &&
                         br.BreedingDate >= now.AddDays(-60))
            .ToListAsync();

        foreach (var br in recentFailedDams)
        {
            var estimatedHeatDate = br.BreedingDate.AddDays(21);
            while (estimatedHeatDate < now)
                estimatedHeatDate = estimatedHeatDate.AddDays(21);

            if (estimatedHeatDate <= cutoff)
            {
                events.Add(new CalendarEventEntry
                {
                    Id = br.DamId,
                    EventType = "Estimated Heat",
                    AnimalTag = br.Dam.TagNumber,
                    AnimalName = br.Dam.Name,
                    EventDate = estimatedHeatDate,
                    Details = "Estimated next heat cycle",
                    Priority = (estimatedHeatDate - now).Days <= 3 ? "High" : "Normal"
                });
            }
        }

        // Overdue pregnancies
        var overdueGestations = await _context.GestationRecords
            .AsNoTracking()
            .Include(g => g.Animal)
            .Where(g => g.FarmId == farmId && g.ExpectedDeliveryDate < now)
            .ToListAsync();

        foreach (var gest in overdueGestations)
        {
            var daysOverdue = (now - gest.ExpectedDeliveryDate).Days;
            events.Add(new CalendarEventEntry
            {
                Id = gest.Id,
                EventType = "Overdue Pregnancy",
                AnimalTag = gest.Animal.TagNumber,
                AnimalName = gest.Animal.Name,
                EventDate = gest.ExpectedDeliveryDate,
                Details = $"Overdue by {daysOverdue} day(s)",
                Priority = "High"
            });
        }

        return Result<List<CalendarEventEntry>>.Success(events.OrderBy(e => e.EventDate).ToList());
    }
}
