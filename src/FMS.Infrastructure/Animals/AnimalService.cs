using FMS.Application.Animal;
using FMS.Application.Common;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Animals;

public class AnimalService : IAnimalService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileStorageService _fileStorage;

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly string[] DocumentExtensions = { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv" };
    private const long MaxDocumentBytes = 20 * 1024 * 1024;

    public AnimalService(FmsDbContext context, ICurrentUserService currentUser, IFileStorageService fileStorage)
    {
        _context = context;
        _currentUser = currentUser;
        _fileStorage = fileStorage;
    }

    public async Task<Result<PagedResult<AnimalListItemDto>>> GetAnimalsAsync(Guid farmId, AnimalListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : Math.Min(filter.PageSize, 200);

        var query = _context.Animals
            .AsNoTracking()
            .Where(a => a.FarmId == farmId && !a.IsDeleted);

        if (!filter.StatusId.HasValue && !filter.IncludeTerminal)
            query = query.Where(a => a.AnimalStatus.Category != AnimalStatusCategory.Terminal);

        if (filter.AnimalTypeId.HasValue)
            query = query.Where(a => a.AnimalTypeId == filter.AnimalTypeId.Value);

        if (filter.BreedId.HasValue)
            query = query.Where(a => a.BreedId == filter.BreedId.Value);

        if (filter.StatusId.HasValue)
            query = query.Where(a => a.AnimalStatusId == filter.StatusId.Value);

        if (filter.LocationId.HasValue)
            query = query.Where(a => a.LocationId == filter.LocationId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(a =>
                a.TagNumber.Contains(term) ||
                (a.Name != null && a.Name.Contains(term)) ||
                a.Identifications.Any(i => i.DateRemoved == null && i.Value.Contains(term)));
        }

        var total = await query.CountAsync();

        var sortBy = filter.SortBy?.Trim().ToLowerInvariant();
        IQueryable<Domain.Entities.Animal> ordered;
        if (filter.SortDescending)
        {
            ordered = sortBy switch
            {
                "tagnumber" => query.OrderByDescending(a => a.TagNumber),
                "name" => query.OrderByDescending(a => a.Name),
                "dateofbirth" => query.OrderByDescending(a => a.DateOfBirth),
                _ => query.OrderByDescending(a => a.CreatedAt)
            };
        }
        else
        {
            ordered = sortBy switch
            {
                "tagnumber" => query.OrderBy(a => a.TagNumber),
                "name" => query.OrderBy(a => a.Name),
                "dateofbirth" => query.OrderBy(a => a.DateOfBirth),
                _ => query.OrderBy(a => a.CreatedAt)
            };
        }

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AnimalListItemDto
            {
                Id = a.Id,
                TagNumber = a.TagNumber,
                Name = a.Name,
                AnimalTypeId = a.AnimalTypeId,
                AnimalTypeName = a.AnimalType.Name,
                BreedId = a.BreedId,
                BreedName = a.Breed != null ? a.Breed.Name : null,
                SexOptionId = a.SexOptionId,
                SexValue = a.SexOption.Value,
                AnimalStatusId = a.AnimalStatusId,
                StatusName = a.AnimalStatus.Name,
                StatusCategory = a.AnimalStatus.Category,
                LocationId = a.LocationId,
                LocationName = a.Location != null ? a.Location.Name : null,
                SireId = a.SireId,
                SireTagNumber = a.Sire != null ? a.Sire.TagNumber : null,
                DamId = a.DamId,
                DamTagNumber = a.Dam != null ? a.Dam.TagNumber : null,
                DateOfBirth = a.DateOfBirth,
                LatestWeightKg = a.WeightRecords
                    .OrderByDescending(w => w.RecordedAt)
                    .Select(w => (decimal?)w.WeightKg)
                    .FirstOrDefault(),
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Result<PagedResult<AnimalListItemDto>>.Success(new PagedResult<AnimalListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        });
    }

    public async Task<Result<AnimalDetailDto>> GetAnimalByIdAsync(Guid farmId, Guid id)
    {
        var animal = await _context.Animals
            .AsNoTracking()
            .Include(a => a.AnimalType)
            .Include(a => a.Breed)
            .Include(a => a.SexOption)
            .Include(a => a.AgeCategory)
            .Include(a => a.AnimalStatus)
            .Include(a => a.Location)
            .Include(a => a.Sire)
            .Include(a => a.Dam)
            .Include(a => a.Identifications).ThenInclude(i => i.IdentificationType)
            .FirstOrDefaultAsync(a => a.Id == id && a.FarmId == farmId && !a.IsDeleted);

        if (animal == null)
            return Result<AnimalDetailDto>.NotFound("Animal not found");

        var weightCount = await _context.WeightRecords.CountAsync(w => w.AnimalId == id);
        var imageCount = await _context.AnimalImages.CountAsync(i => i.AnimalId == id);
        var documentCount = await _context.AnimalDocuments.CountAsync(d => d.AnimalId == id);

        return Result<AnimalDetailDto>.Success(MapDetail(animal, weightCount, imageCount, documentCount));
    }

    public async Task<Result<AnimalDetailDto>> CreateAnimalAsync(Guid farmId, CreateAnimalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TagNumber))
            return Result<AnimalDetailDto>.Validation("Tag number is required");

        var tag = request.TagNumber.Trim();

        var tagExists = await _context.Animals
            .AnyAsync(a => a.FarmId == farmId && a.TagNumber == tag && !a.IsDeleted);
        if (tagExists)
            return Result<AnimalDetailDto>.Conflict($"An animal with tag '{tag}' already exists in this farm");

        var animalType = await _context.AnimalTypes
            .FirstOrDefaultAsync(at => at.Id == request.AnimalTypeId && at.FarmId == farmId);
        if (animalType == null)
            return Result<AnimalDetailDto>.NotFound("Animal type not found");

        if (request.BreedId.HasValue)
        {
            var breed = await _context.Breeds
                .FirstOrDefaultAsync(b => b.Id == request.BreedId.Value && b.AnimalTypeId == request.AnimalTypeId);
            if (breed == null)
                return Result<AnimalDetailDto>.Validation("Breed not found or does not belong to the selected animal type");
        }

        var sex = await _context.SexOptions
            .FirstOrDefaultAsync(s => s.Id == request.SexOptionId && s.FarmId == farmId);
        if (sex == null)
            return Result<AnimalDetailDto>.NotFound("Sex option not found");

        if (request.AgeCategoryId.HasValue)
        {
            var ageCategory = await _context.AgeCategories
                .FirstOrDefaultAsync(ac => ac.Id == request.AgeCategoryId.Value && ac.FarmId == farmId);
            if (ageCategory == null)
                return Result<AnimalDetailDto>.NotFound("Age category not found");
        }

        var status = await _context.AnimalStatuses
            .FirstOrDefaultAsync(s => s.Id == request.AnimalStatusId && s.FarmId == farmId);
        if (status == null)
            return Result<AnimalDetailDto>.NotFound("Animal status not found");

        if (request.LocationId.HasValue)
        {
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.Id == request.LocationId.Value && l.FarmId == farmId);
            if (location == null)
                return Result<AnimalDetailDto>.NotFound("Location not found");
        }

        if (request.SireId.HasValue)
        {
            var sire = await _context.Animals
                .FirstOrDefaultAsync(a => a.Id == request.SireId.Value && a.FarmId == farmId && !a.IsDeleted);
            if (sire == null)
                return Result<AnimalDetailDto>.NotFound("Sire animal not found");
        }

        if (request.DamId.HasValue)
        {
            var dam = await _context.Animals
                .FirstOrDefaultAsync(a => a.Id == request.DamId.Value && a.FarmId == farmId && !a.IsDeleted);
            if (dam == null)
                return Result<AnimalDetailDto>.NotFound("Dam animal not found");
        }

        var identificationValidation = await ValidateIdentificationValuesAsync(farmId, request.Identifications);
        if (!identificationValidation.IsSuccess)
            return Result<AnimalDetailDto>.Failure(identificationValidation.Error!);

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var animal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            TagNumber = tag,
            Name = request.Name?.Trim(),
            AnimalTypeId = request.AnimalTypeId,
            BreedId = request.BreedId,
            SexOptionId = request.SexOptionId,
            AgeCategoryId = request.AgeCategoryId,
            AnimalStatusId = request.AnimalStatusId,
            LocationId = request.LocationId,
            SireId = request.SireId,
            DamId = request.DamId,
            DateOfBirth = request.DateOfBirth,
            AcquisitionDate = request.AcquisitionDate,
            Notes = request.Notes,
            CreatedAt = now,
            CreatedBy = userId
        };

        foreach (var identification in request.Identifications)
        {
            animal.Identifications.Add(new AnimalIdentification
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                IdentificationTypeId = identification.IdentificationTypeId,
                Value = identification.Value.Trim(),
                IsPrimary = identification.IsPrimary,
                DateAttached = now,
                CreatedAt = now
            });
        }
        EnsureSinglePrimary(animal.Identifications);

        animal.TimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            EventType = TimelineEventTypes.Created,
            Title = "Animal registered",
            Description = $"Animal registered with tag '{tag}'",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        _context.Animals.Add(animal);
        await _context.SaveChangesAsync();

        return await GetAnimalByIdAsync(farmId, animal.Id);
    }

    public async Task<Result<AnimalDetailDto>> UpdateAnimalAsync(Guid farmId, Guid id, UpdateAnimalRequest request)
    {
        var animal = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == id && a.FarmId == farmId && !a.IsDeleted);
        if (animal == null)
            return Result<AnimalDetailDto>.NotFound("Animal not found");

        if (string.IsNullOrWhiteSpace(request.TagNumber))
            return Result<AnimalDetailDto>.Validation("Tag number is required");

        var tag = request.TagNumber.Trim();
        var tagExists = await _context.Animals
            .AnyAsync(a => a.FarmId == farmId && a.TagNumber == tag && a.Id != id && !a.IsDeleted);
        if (tagExists)
            return Result<AnimalDetailDto>.Conflict($"An animal with tag '{tag}' already exists in this farm");

        if (request.BreedId.HasValue)
        {
            var breed = await _context.Breeds
                .FirstOrDefaultAsync(b => b.Id == request.BreedId.Value && b.AnimalTypeId == animal.AnimalTypeId);
            if (breed == null)
                return Result<AnimalDetailDto>.Validation("Breed not found or does not belong to the animal's type");
        }

        var sex = await _context.SexOptions
            .FirstOrDefaultAsync(s => s.Id == request.SexOptionId && s.FarmId == farmId);
        if (sex == null)
            return Result<AnimalDetailDto>.NotFound("Sex option not found");

        if (request.AgeCategoryId.HasValue)
        {
            var ageCategory = await _context.AgeCategories
                .FirstOrDefaultAsync(ac => ac.Id == request.AgeCategoryId.Value && ac.FarmId == farmId);
            if (ageCategory == null)
                return Result<AnimalDetailDto>.NotFound("Age category not found");
        }

        if (request.LocationId.HasValue)
        {
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.Id == request.LocationId.Value && l.FarmId == farmId);
            if (location == null)
                return Result<AnimalDetailDto>.NotFound("Location not found");
        }

        if (request.SireId.HasValue)
        {
            var sire = await _context.Animals
                .FirstOrDefaultAsync(a => a.Id == request.SireId.Value && a.FarmId == farmId && !a.IsDeleted);
            if (sire == null)
                return Result<AnimalDetailDto>.NotFound("Sire animal not found");
        }

        if (request.DamId.HasValue)
        {
            var dam = await _context.Animals
                .FirstOrDefaultAsync(a => a.Id == request.DamId.Value && a.FarmId == farmId && !a.IsDeleted);
            if (dam == null)
                return Result<AnimalDetailDto>.NotFound("Dam animal not found");
        }

        animal.TagNumber = tag;
        animal.Name = request.Name?.Trim();
        animal.BreedId = request.BreedId;
        animal.SexOptionId = request.SexOptionId;
        animal.AgeCategoryId = request.AgeCategoryId;
        animal.LocationId = request.LocationId;
        animal.SireId = request.SireId;
        animal.DamId = request.DamId;
        animal.DateOfBirth = request.DateOfBirth;
        animal.AcquisitionDate = request.AcquisitionDate;
        animal.Notes = request.Notes;
        animal.ModifiedAt = DateTime.UtcNow;
        animal.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return await GetAnimalByIdAsync(farmId, id);
    }

    public async Task<Result> DeleteAnimalAsync(Guid farmId, Guid id)
    {
        var animal = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == id && a.FarmId == farmId && !a.IsDeleted);
        if (animal == null)
            return Result.NotFound("Animal not found");

        animal.IsDeleted = true;
        animal.DeletedAt = DateTime.UtcNow;
        animal.DeletedBy = _currentUser.GetUserId();
        animal.ModifiedAt = DateTime.UtcNow;
        animal.ModifiedBy = animal.DeletedBy;

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<AnimalDetailDto>> ChangeStatusAsync(Guid farmId, Guid id, ChangeAnimalStatusRequest request)
    {
        var animal = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == id && a.FarmId == farmId && !a.IsDeleted);
        if (animal == null)
            return Result<AnimalDetailDto>.NotFound("Animal not found");

        var newStatus = await _context.AnimalStatuses
            .FirstOrDefaultAsync(s => s.Id == request.NewStatusId && s.FarmId == farmId);
        if (newStatus == null)
            return Result<AnimalDetailDto>.NotFound("Animal status not found");

        if (animal.AnimalStatusId == newStatus.Id)
            return Result<AnimalDetailDto>.Validation("Animal already has this status");

        var oldStatusName = await _context.AnimalStatuses
            .Where(s => s.Id == animal.AnimalStatusId)
            .Select(s => s.Name)
            .FirstAsync();

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        animal.AnimalStatusId = newStatus.Id;
        animal.ModifiedAt = now;
        animal.ModifiedBy = userId;

        var timelineEvent = new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = farmId,
            EventType = TimelineEventTypes.StatusChanged,
            Title = $"{oldStatusName} → {newStatus.Name}",
            Description = request.Reason,
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        };
        _context.AnimalTimelineEvents.Add(timelineEvent);

        await _context.SaveChangesAsync();

        return await GetAnimalByIdAsync(farmId, id);
    }

    public async Task<Result<AnimalIdentificationDto>> AddIdentificationAsync(Guid farmId, Guid animalId, AddAnimalIdentificationRequest request)
    {
        var animal = await _context.Animals
            .Include(a => a.Identifications)
            .FirstOrDefaultAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (animal == null)
            return Result<AnimalIdentificationDto>.NotFound("Animal not found");

        if (string.IsNullOrWhiteSpace(request.Value))
            return Result<AnimalIdentificationDto>.Validation("Identification value is required");

        var identificationType = await _context.IdentificationTypes
            .FirstOrDefaultAsync(it => it.Id == request.IdentificationTypeId && it.FarmId == farmId);
        if (identificationType == null)
            return Result<AnimalIdentificationDto>.NotFound("Identification type not found");

        var value = request.Value.Trim();
        var duplicate = await _context.AnimalIdentifications
            .AnyAsync(i => i.FarmId == farmId &&
                           i.IdentificationTypeId == request.IdentificationTypeId &&
                           i.Value == value &&
                           i.DateRemoved == null);
        if (duplicate)
            return Result<AnimalIdentificationDto>.Conflict(
                $"An active identification with value '{value}' already exists in this farm");

        var now = DateTime.UtcNow;
        var identification = new AnimalIdentification
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = farmId,
            IdentificationTypeId = request.IdentificationTypeId,
            Value = value,
            IsPrimary = request.IsPrimary,
            DateAttached = request.DateAttached ?? now,
            CreatedAt = now
        };

        if (request.IsPrimary)
        {
            foreach (var existing in animal.Identifications.Where(i => i.IsPrimary && i.DateRemoved == null))
                existing.IsPrimary = false;
        }

        _context.AnimalIdentifications.Add(identification);
        await _context.SaveChangesAsync();

        return Result<AnimalIdentificationDto>.Success(new AnimalIdentificationDto
        {
            Id = identification.Id,
            IdentificationTypeId = identification.IdentificationTypeId,
            IdentificationTypeName = identificationType.Name,
            Value = identification.Value,
            IsPrimary = identification.IsPrimary,
            DateAttached = identification.DateAttached,
            DateRemoved = identification.DateRemoved
        });
    }

    public async Task<Result> RemoveIdentificationAsync(Guid farmId, Guid animalId, Guid identificationId)
    {
        var identification = await _context.AnimalIdentifications
            .FirstOrDefaultAsync(i => i.Id == identificationId && i.AnimalId == animalId && i.FarmId == farmId);

        if (identification == null || identification.DateRemoved != null)
            return Result.NotFound("Identification not found");

        identification.DateRemoved = DateTime.UtcNow;
        identification.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<PagedResult<WeightRecordDto>>> GetWeightsAsync(Guid farmId, Guid animalId, int page, int pageSize)
    {
        var animalExists = await _context.Animals
            .AnyAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (!animalExists)
            return Result<PagedResult<WeightRecordDto>>.NotFound("Animal not found");

        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);

        var records = await _context.WeightRecords
            .AsNoTracking()
            .Where(w => w.AnimalId == animalId)
            .ToListAsync();

        var chronological = records.OrderBy(w => w.RecordedAt).ThenBy(w => w.CreatedAt).ToList();
        var previousWeights = new Dictionary<Guid, decimal>();
        for (var i = 1; i < chronological.Count; i++)
            previousWeights[chronological[i].Id] = chronological[i - 1].WeightKg;

        var dtos = records
            .OrderByDescending(w => w.RecordedAt)
            .ThenByDescending(w => w.CreatedAt)
            .Select(w => new WeightRecordDto
            {
                Id = w.Id,
                AnimalId = w.AnimalId,
                WeightKg = w.WeightKg,
                RecordedAt = w.RecordedAt,
                ChangeFromPreviousKg = previousWeights.TryGetValue(w.Id, out var previous)
                    ? Math.Round(w.WeightKg - previous, 2)
                    : null,
                Notes = w.Notes,
                CreatedAt = w.CreatedAt
            })
            .ToList();

        return Result<PagedResult<WeightRecordDto>>.Success(new PagedResult<WeightRecordDto>
        {
            Items = dtos.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = dtos.Count
        });
    }

    public async Task<Result<WeightRecordDto>> AddWeightAsync(Guid farmId, Guid animalId, CreateWeightRecordRequest request)
    {
        var animal = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (animal == null)
            return Result<WeightRecordDto>.NotFound("Animal not found");

        if (request.WeightKg <= 0)
            return Result<WeightRecordDto>.Validation("Weight must be greater than zero");

        if (request.RecordedAt != default && request.RecordedAt > DateTime.UtcNow.AddMinutes(5))
            return Result<WeightRecordDto>.Validation("Recorded date cannot be in the future");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;
        var recordedAt = request.RecordedAt == default ? now : request.RecordedAt;
        var weight = Math.Round(request.WeightKg, 2);

        var record = new WeightRecord
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = farmId,
            WeightKg = weight,
            RecordedAt = recordedAt,
            Notes = request.Notes,
            CreatedAt = now,
            CreatedBy = userId
        };
        _context.WeightRecords.Add(record);

        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = farmId,
            EventType = TimelineEventTypes.WeightRecorded,
            Title = $"Weight recorded: {weight} kg",
            Description = request.Notes,
            RelatedEntityId = record.Id,
            RelatedEntityType = "WeightRecord",
            OccurredAt = recordedAt,
            CreatedAt = now,
            CreatedBy = userId
        });

        await _context.SaveChangesAsync();

        var previousWeight = await _context.WeightRecords
            .AsNoTracking()
            .Where(w => w.AnimalId == animal.Id && w.Id != record.Id && w.RecordedAt < record.RecordedAt)
            .OrderByDescending(w => w.RecordedAt)
            .Select(w => (decimal?)w.WeightKg)
            .FirstOrDefaultAsync();

        return Result<WeightRecordDto>.Success(new WeightRecordDto
        {
            Id = record.Id,
            AnimalId = record.AnimalId,
            WeightKg = record.WeightKg,
            RecordedAt = record.RecordedAt,
            ChangeFromPreviousKg = previousWeight.HasValue
                ? Math.Round(record.WeightKg - previousWeight.Value, 2)
                : null,
            Notes = record.Notes,
            CreatedAt = record.CreatedAt
        });
    }

    public async Task<Result<WeightRecordDto>> UpdateWeightAsync(Guid farmId, Guid animalId, Guid weightId, UpdateWeightRecordRequest request)
    {
        var record = await _context.WeightRecords
            .FirstOrDefaultAsync(w => w.Id == weightId && w.AnimalId == animalId && w.FarmId == farmId);
        if (record == null)
            return Result<WeightRecordDto>.NotFound("Weight record not found");

        if (request.WeightKg <= 0)
            return Result<WeightRecordDto>.Validation("Weight must be greater than zero");

        if (request.RecordedAt != default && request.RecordedAt > DateTime.UtcNow.AddMinutes(5))
            return Result<WeightRecordDto>.Validation("Recorded date cannot be in the future");

        var oldWeight = record.WeightKg;
        var newWeight = Math.Round(request.WeightKg, 2);
        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        record.WeightKg = newWeight;
        record.RecordedAt = request.RecordedAt == default ? record.RecordedAt : request.RecordedAt;
        record.Notes = request.Notes;
        record.ModifiedAt = now;
        record.ModifiedBy = userId;

        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animalId,
            FarmId = farmId,
            EventType = TimelineEventTypes.WeightUpdated,
            Title = $"Weight updated: {oldWeight} kg → {newWeight} kg",
            RelatedEntityId = record.Id,
            RelatedEntityType = "WeightRecord",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        await _context.SaveChangesAsync();

        return Result<WeightRecordDto>.Success(new WeightRecordDto
        {
            Id = record.Id,
            AnimalId = record.AnimalId,
            WeightKg = record.WeightKg,
            RecordedAt = record.RecordedAt,
            Notes = record.Notes,
            CreatedAt = record.CreatedAt
        });
    }

    public async Task<Result> DeleteWeightAsync(Guid farmId, Guid animalId, Guid weightId)
    {
        var record = await _context.WeightRecords
            .FirstOrDefaultAsync(w => w.Id == weightId && w.AnimalId == animalId && w.FarmId == farmId);
        if (record == null)
            return Result.NotFound("Weight record not found");

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        _context.WeightRecords.Remove(record);

        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animalId,
            FarmId = farmId,
            EventType = TimelineEventTypes.WeightDeleted,
            Title = $"Weight entry deleted: {record.WeightKg} kg ({record.RecordedAt:yyyy-MM-dd})",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<WeightReportDto>> GetWeightReportAsync(Guid farmId, Guid animalId)
    {
        var animal = await _context.Animals
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (animal == null)
            return Result<WeightReportDto>.NotFound("Animal not found");

        var points = await _context.WeightRecords
            .AsNoTracking()
            .Where(w => w.AnimalId == animalId)
            .OrderBy(w => w.RecordedAt)
            .Select(w => new WeightPointDto { RecordedAt = w.RecordedAt, WeightKg = w.WeightKg })
            .ToListAsync();

        var report = new WeightReportDto
        {
            AnimalId = animal.Id,
            TagNumber = animal.TagNumber,
            Name = animal.Name,
            RecordCount = points.Count,
            Points = points
        };

        if (points.Count == 0)
            return Result<WeightReportDto>.Success(report);

        var first = points[0];
        var last = points[^1];

        report.FirstWeightKg = first.WeightKg;
        report.FirstRecordedAt = first.RecordedAt;
        report.LastWeightKg = last.WeightKg;
        report.LastRecordedAt = last.RecordedAt;

        if (points.Count > 1)
        {
            report.TotalGainKg = Math.Round(last.WeightKg - first.WeightKg, 2);

            var days = (last.RecordedAt - first.RecordedAt).TotalDays;
            if (days > 0)
                report.AverageDailyGainKg = Math.Round((double)(last.WeightKg - first.WeightKg) / days, 3);
        }

        return Result<WeightReportDto>.Success(report);
    }

    public async Task<Result<AnimalImageDto>> AddImageAsync(Guid farmId, Guid animalId, Stream content, string originalFileName, string contentType, long contentLength, string? caption, bool isPrimary)
    {
        var animalExists = await _context.Animals
            .AnyAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (!animalExists)
            return Result<AnimalImageDto>.NotFound("Animal not found");

        var saved = await _fileStorage.SaveAsync(
            $"farms/{farmId}/animals/{animalId}/images",
            content, originalFileName, contentType, MaxImageBytes, ImageExtensions);
        if (!saved.IsSuccess)
            return Result<AnimalImageDto>.Failure(saved.Error!);

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        if (isPrimary)
            await DemotePrimaryImagesAsync(animalId);

        var image = new AnimalImage
        {
            Id = Guid.NewGuid(),
            AnimalId = animalId,
            FarmId = farmId,
            StoragePath = saved.Value!.StoragePath,
            FileName = saved.Value.FileName,
            OriginalFileName = saved.Value.OriginalFileName,
            ContentType = saved.Value.ContentType,
            FileSizeBytes = contentLength,
            Caption = caption,
            IsPrimary = isPrimary,
            CreatedAt = now,
            CreatedBy = userId
        };
        _context.AnimalImages.Add(image);

        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animalId,
            FarmId = farmId,
            EventType = TimelineEventTypes.ImageAdded,
            Title = $"Image added: {image.OriginalFileName}",
            RelatedEntityId = image.Id,
            RelatedEntityType = "AnimalImage",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        await _context.SaveChangesAsync();

        return Result<AnimalImageDto>.Success(MapImage(image));
    }

    public async Task<Result<List<AnimalImageDto>>> GetImagesAsync(Guid farmId, Guid animalId)
    {
        var animalExists = await _context.Animals
            .AnyAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (!animalExists)
            return Result<List<AnimalImageDto>>.NotFound("Animal not found");

        var images = await _context.AnimalImages
            .AsNoTracking()
            .Where(i => i.AnimalId == animalId)
            .OrderByDescending(i => i.IsPrimary)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Result<List<AnimalImageDto>>.Success(images.Select(MapImage).ToList());
    }

    public async Task<Result> DeleteImageAsync(Guid farmId, Guid animalId, Guid imageId)
    {
        var image = await _context.AnimalImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.AnimalId == animalId && i.FarmId == farmId);
        if (image == null)
            return Result.NotFound("Image not found");

        _fileStorage.Delete(image.StoragePath);
        _context.AnimalImages.Remove(image);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<AnimalImageDto>> SetPrimaryImageAsync(Guid farmId, Guid animalId, Guid imageId)
    {
        var image = await _context.AnimalImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.AnimalId == animalId && i.FarmId == farmId);
        if (image == null)
            return Result<AnimalImageDto>.NotFound("Image not found");

        await DemotePrimaryImagesAsync(animalId);

        image.IsPrimary = true;
        image.ModifiedAt = DateTime.UtcNow;
        image.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<AnimalImageDto>.Success(MapImage(image));
    }

    public async Task<Result<AnimalDocumentDto>> AddDocumentAsync(Guid farmId, Guid animalId, Stream content, string originalFileName, string contentType, long contentLength, string category, string? description)
    {
        var animalExists = await _context.Animals
            .AnyAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (!animalExists)
            return Result<AnimalDocumentDto>.NotFound("Animal not found");

        if (string.IsNullOrWhiteSpace(category))
            return Result<AnimalDocumentDto>.Validation("Category is required");

        var saved = await _fileStorage.SaveAsync(
            $"farms/{farmId}/animals/{animalId}/documents",
            content, originalFileName, contentType, MaxDocumentBytes, DocumentExtensions);
        if (!saved.IsSuccess)
            return Result<AnimalDocumentDto>.Failure(saved.Error!);

        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        var document = new AnimalDocument
        {
            Id = Guid.NewGuid(),
            AnimalId = animalId,
            FarmId = farmId,
            Category = category.Trim(),
            StoragePath = saved.Value!.StoragePath,
            FileName = saved.Value.FileName,
            OriginalFileName = saved.Value.OriginalFileName,
            ContentType = saved.Value.ContentType,
            FileSizeBytes = contentLength,
            Description = description,
            CreatedAt = now,
            CreatedBy = userId
        };
        _context.AnimalDocuments.Add(document);

        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animalId,
            FarmId = farmId,
            EventType = TimelineEventTypes.DocumentAdded,
            Title = $"Document added: {document.OriginalFileName} ({document.Category})",
            RelatedEntityId = document.Id,
            RelatedEntityType = "AnimalDocument",
            OccurredAt = now,
            CreatedAt = now,
            CreatedBy = userId
        });

        await _context.SaveChangesAsync();

        return Result<AnimalDocumentDto>.Success(MapDocument(document));
    }

    public async Task<Result<List<AnimalDocumentDto>>> GetDocumentsAsync(Guid farmId, Guid animalId, string? category)
    {
        var animalExists = await _context.Animals
            .AnyAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (!animalExists)
            return Result<List<AnimalDocumentDto>>.NotFound("Animal not found");

        var query = _context.AnimalDocuments.AsNoTracking().Where(d => d.AnimalId == animalId);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(d => d.Category.ToLower() == category.Trim().ToLower());

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        return Result<List<AnimalDocumentDto>>.Success(documents.Select(MapDocument).ToList());
    }

    public async Task<Result> DeleteDocumentAsync(Guid farmId, Guid animalId, Guid documentId)
    {
        var document = await _context.AnimalDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.AnimalId == animalId && d.FarmId == farmId);
        if (document == null)
            return Result.NotFound("Document not found");

        _fileStorage.Delete(document.StoragePath);
        _context.AnimalDocuments.Remove(document);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<AnimalDocumentDto>> GetDocumentForDownloadAsync(Guid farmId, Guid animalId, Guid documentId)
    {
        var document = await _context.AnimalDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.AnimalId == animalId && d.FarmId == farmId);
        if (document == null)
            return Result<AnimalDocumentDto>.NotFound("Document not found");

        if (!_fileStorage.Exists(document.StoragePath))
            return Result<AnimalDocumentDto>.NotFound("Document file is missing from storage");

        return Result<AnimalDocumentDto>.Success(MapDocument(document));
    }

    public async Task<Result<AnimalTransferDto>> TransferAnimalAsync(Guid farmId, Guid animalId, TransferAnimalRequest request)
    {
        var animal = await _context.Animals
            .FirstOrDefaultAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (animal == null)
            return Result<AnimalTransferDto>.NotFound("Animal not found");

        var toLocation = await _context.Locations
            .FirstOrDefaultAsync(l => l.Id == request.ToLocationId && l.FarmId == farmId);
        if (toLocation == null)
            return Result<AnimalTransferDto>.NotFound("Destination location not found");

        if (animal.LocationId == request.ToLocationId)
            return Result<AnimalTransferDto>.Validation("Animal is already in this location");

        if (request.TransferredAt.HasValue && request.TransferredAt.Value > DateTime.UtcNow.AddMinutes(5))
            return Result<AnimalTransferDto>.Validation("Transfer date cannot be in the future");

        var transferredAt = request.TransferredAt ?? DateTime.UtcNow;
        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        string? fromLocationName = null;
        if (animal.LocationId.HasValue)
        {
            fromLocationName = await _context.Locations
                .Where(l => l.Id == animal.LocationId.Value)
                .Select(l => l.Name)
                .FirstOrDefaultAsync();
        }

        var transfer = new AnimalTransfer
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = farmId,
            FromLocationId = animal.LocationId,
            ToLocationId = toLocation.Id,
            TransferredAt = transferredAt,
            Reason = request.Reason,
            CreatedAt = now,
            CreatedBy = userId
        };
        _context.AnimalTransfers.Add(transfer);

        animal.LocationId = toLocation.Id;
        animal.ModifiedAt = now;
        animal.ModifiedBy = userId;

        _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = farmId,
            EventType = TimelineEventTypes.Transferred,
            Title = $"Moved: {fromLocationName ?? "Unassigned"} → {toLocation.Name}",
            Description = request.Reason,
            RelatedEntityId = transfer.Id,
            RelatedEntityType = "AnimalTransfer",
            OccurredAt = transferredAt,
            CreatedAt = now,
            CreatedBy = userId
        });

        await _context.SaveChangesAsync();

        return Result<AnimalTransferDto>.Success(new AnimalTransferDto
        {
            Id = transfer.Id,
            AnimalId = transfer.AnimalId,
            FromLocationId = transfer.FromLocationId,
            FromLocationName = fromLocationName,
            ToLocationId = transfer.ToLocationId,
            ToLocationName = toLocation.Name,
            TransferredAt = transfer.TransferredAt,
            Reason = transfer.Reason
        });
    }

    public async Task<Result<BulkOperationResultDto>> BulkChangeStatusAsync(Guid farmId, BulkStatusChangeRequest request)
    {
        if (request.AnimalIds.Count == 0)
            return Result<BulkOperationResultDto>.Validation("No animals selected");

        var status = await _context.AnimalStatuses
            .FirstOrDefaultAsync(s => s.Id == request.NewStatusId && s.FarmId == farmId);
        if (status == null)
            return Result<BulkOperationResultDto>.NotFound("Animal status not found");

        var ids = request.AnimalIds.Distinct().ToList();
        var animals = await _context.Animals
            .Include(a => a.AnimalStatus)
            .Where(a => ids.Contains(a.Id) && a.FarmId == farmId && !a.IsDeleted)
            .ToListAsync();

        var result = new BulkOperationResultDto { RequestedCount = ids.Count };
        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        foreach (var id in ids)
        {
            var animal = animals.FirstOrDefault(a => a.Id == id);
            if (animal == null)
            {
                result.Failures.Add(new BulkOperationFailureDto { AnimalId = id, Reason = "Animal not found" });
                continue;
            }
            if (animal.AnimalStatusId == status.Id)
            {
                result.Failures.Add(new BulkOperationFailureDto { AnimalId = id, Reason = "Already in this status" });
                continue;
            }

            var oldStatusName = animal.AnimalStatus.Name;
            animal.AnimalStatusId = status.Id;
            animal.ModifiedAt = now;
            animal.ModifiedBy = userId;

            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = animal.Id,
                FarmId = farmId,
                EventType = TimelineEventTypes.StatusChanged,
                Title = $"{oldStatusName} → {status.Name}",
                Description = request.Reason,
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = userId
            });

            result.SuccessCount++;
        }

        await _context.SaveChangesAsync();

        return Result<BulkOperationResultDto>.Success(result);
    }

    public async Task<Result<BulkOperationResultDto>> BulkTransferAsync(Guid farmId, BulkTransferRequest request)
    {
        if (request.AnimalIds.Count == 0)
            return Result<BulkOperationResultDto>.Validation("No animals selected");

        var toLocation = await _context.Locations
            .FirstOrDefaultAsync(l => l.Id == request.ToLocationId && l.FarmId == farmId);
        if (toLocation == null)
            return Result<BulkOperationResultDto>.NotFound("Destination location not found");

        var ids = request.AnimalIds.Distinct().ToList();
        var animals = await _context.Animals
            .Include(a => a.Location)
            .Where(a => ids.Contains(a.Id) && a.FarmId == farmId && !a.IsDeleted)
            .ToListAsync();

        var result = new BulkOperationResultDto { RequestedCount = ids.Count };
        var userId = _currentUser.GetUserId();
        var now = DateTime.UtcNow;

        foreach (var id in ids)
        {
            var animal = animals.FirstOrDefault(a => a.Id == id);
            if (animal == null)
            {
                result.Failures.Add(new BulkOperationFailureDto { AnimalId = id, Reason = "Animal not found" });
                continue;
            }
            if (animal.LocationId == toLocation.Id)
            {
                result.Failures.Add(new BulkOperationFailureDto { AnimalId = id, Reason = "Already in this location" });
                continue;
            }

            var fromLocationName = animal.Location?.Name;

            var transfer = new AnimalTransfer
            {
                Id = Guid.NewGuid(),
                AnimalId = animal.Id,
                FarmId = farmId,
                FromLocationId = animal.LocationId,
                ToLocationId = toLocation.Id,
                TransferredAt = now,
                Reason = request.Reason,
                CreatedAt = now,
                CreatedBy = userId
            };
            _context.AnimalTransfers.Add(transfer);

            animal.LocationId = toLocation.Id;
            animal.ModifiedAt = now;
            animal.ModifiedBy = userId;

            _context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
            {
                Id = Guid.NewGuid(),
                AnimalId = animal.Id,
                FarmId = farmId,
                EventType = TimelineEventTypes.Transferred,
                Title = $"Moved: {fromLocationName ?? "Unassigned"} → {toLocation.Name}",
                Description = request.Reason,
                RelatedEntityId = transfer.Id,
                RelatedEntityType = "AnimalTransfer",
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = userId
            });

            result.SuccessCount++;
        }

        await _context.SaveChangesAsync();

        return Result<BulkOperationResultDto>.Success(result);
    }

    public async Task<Result<PagedResult<AnimalTimelineEventDto>>> GetTimelineAsync(Guid farmId, Guid animalId, int page, int pageSize, string? eventType)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var animalExists = await _context.Animals
            .AnyAsync(a => a.Id == animalId && a.FarmId == farmId && !a.IsDeleted);
        if (!animalExists)
            return Result<PagedResult<AnimalTimelineEventDto>>.NotFound("Animal not found");

        var query = _context.AnimalTimelineEvents
            .AsNoTracking()
            .Where(e => e.AnimalId == animalId && e.FarmId == farmId);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType.Trim());

        var totalCount = await query.CountAsync();

        var events = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AnimalTimelineEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                Title = e.Title,
                Description = e.Description,
                RelatedEntityId = e.RelatedEntityId,
                RelatedEntityType = e.RelatedEntityType,
                OccurredAt = e.OccurredAt
            })
            .ToListAsync();

        return Result<PagedResult<AnimalTimelineEventDto>>.Success(new PagedResult<AnimalTimelineEventDto>
        {
            Items = events,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    private async Task DemotePrimaryImagesAsync(Guid animalId)
    {
        var primaries = await _context.AnimalImages
            .Where(i => i.AnimalId == animalId && i.IsPrimary)
            .ToListAsync();

        foreach (var primary in primaries)
        {
            primary.IsPrimary = false;
            primary.ModifiedAt = DateTime.UtcNow;
            primary.ModifiedBy = _currentUser.GetUserId();
        }
    }

    private static AnimalImageDto MapImage(AnimalImage image) => new()
    {
        Id = image.Id,
        AnimalId = image.AnimalId,
        Url = $"/uploads/{image.StoragePath}",
        OriginalFileName = image.OriginalFileName,
        ContentType = image.ContentType,
        FileSizeBytes = image.FileSizeBytes,
        Caption = image.Caption,
        IsPrimary = image.IsPrimary,
        CreatedAt = image.CreatedAt
    };

    private static AnimalDocumentDto MapDocument(AnimalDocument document) => new()
    {
        Id = document.Id,
        AnimalId = document.AnimalId,
        Category = document.Category,
        Url = $"/uploads/{document.StoragePath}",
        StoragePath = document.StoragePath,
        OriginalFileName = document.OriginalFileName,
        ContentType = document.ContentType,
        FileSizeBytes = document.FileSizeBytes,
        Description = document.Description,
        CreatedAt = document.CreatedAt
    };

    private async Task<Result> ValidateIdentificationValuesAsync(Guid farmId, List<CreateAnimalIdentificationRequest> identifications)
    {
        if (identifications.Count == 0)
            return Result.Success();

        foreach (var identification in identifications)
        {
            if (string.IsNullOrWhiteSpace(identification.Value))
                return Result.Validation("Identification value is required");
        }

        var duplicatesInRequest = identifications
            .GroupBy(i => new { i.IdentificationTypeId, Value = i.Value.Trim() })
            .Any(g => g.Count() > 1);
        if (duplicatesInRequest)
            return Result.Conflict("Duplicate identification values in request");

        var typeIds = identifications.Select(i => i.IdentificationTypeId).Distinct().ToList();
        var foundTypeCount = await _context.IdentificationTypes
            .CountAsync(it => it.FarmId == farmId && typeIds.Contains(it.Id));
        if (foundTypeCount != typeIds.Count)
            return Result.NotFound("Identification type not found");

        foreach (var identification in identifications)
        {
            var exists = await _context.AnimalIdentifications
                .AnyAsync(i => i.FarmId == farmId &&
                               i.IdentificationTypeId == identification.IdentificationTypeId &&
                               i.Value == identification.Value.Trim() &&
                               i.DateRemoved == null);
            if (exists)
                return Result.Conflict(
                    $"An active identification with value '{identification.Value.Trim()}' already exists in this farm");
        }

        return Result.Success();
    }

    private static void EnsureSinglePrimary(ICollection<AnimalIdentification> identifications)
    {
        var primaryAssigned = false;
        foreach (var identification in identifications)
        {
            if (identification.IsPrimary && !primaryAssigned)
            {
                primaryAssigned = true;
            }
            else
            {
                identification.IsPrimary = false;
            }
        }
    }

    private static AnimalDetailDto MapDetail(Domain.Entities.Animal animal, int weightCount, int imageCount, int documentCount)
    {
        return new AnimalDetailDto
        {
            Id = animal.Id,
            FarmId = animal.FarmId,
            TagNumber = animal.TagNumber,
            Name = animal.Name,
            AnimalTypeId = animal.AnimalTypeId,
            AnimalTypeName = animal.AnimalType.Name,
            BreedId = animal.BreedId,
            BreedName = animal.Breed?.Name,
            SexOptionId = animal.SexOptionId,
            SexValue = animal.SexOption.Value,
            AgeCategoryId = animal.AgeCategoryId,
            AgeCategoryName = animal.AgeCategory?.Name,
            AnimalStatusId = animal.AnimalStatusId,
            StatusName = animal.AnimalStatus.Name,
            StatusCategory = animal.AnimalStatus.Category,
            LocationId = animal.LocationId,
            LocationName = animal.Location?.Name,
            SireId = animal.SireId,
            SireTagNumber = animal.Sire?.TagNumber,
            DamId = animal.DamId,
            DamTagNumber = animal.Dam?.TagNumber,
            DateOfBirth = animal.DateOfBirth,
            AcquisitionDate = animal.AcquisitionDate,
            Notes = animal.Notes,
            Identifications = animal.Identifications
                .Where(i => i.DateRemoved == null)
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.CreatedAt)
                .Select(i => new AnimalIdentificationDto
                {
                    Id = i.Id,
                    IdentificationTypeId = i.IdentificationTypeId,
                    IdentificationTypeName = i.IdentificationType.Name,
                    Value = i.Value,
                    IsPrimary = i.IsPrimary,
                    DateAttached = i.DateAttached,
                    DateRemoved = i.DateRemoved
                })
                .ToList(),
            WeightRecordsCount = weightCount,
            ImagesCount = imageCount,
            DocumentsCount = documentCount,
            CreatedAt = animal.CreatedAt
        };
    }
}
