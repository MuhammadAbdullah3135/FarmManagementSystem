using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FMS.Infrastructure.Configuration;

public class ConfigurationService : IConfigurationService
{
    private readonly FmsDbContext _context;

    public ConfigurationService(FmsDbContext context)
    {
        _context = context;
    }

    // Animal Types
    public async Task<Result<List<AnimalTypeDto>>> GetAnimalTypesAsync(Guid farmId)
    {
        var types = await _context.AnimalTypes
            .Include(at => at.Breeds)
            .Where(at => at.FarmId == farmId)
            .Select(at => new AnimalTypeDto
            {
                Id = at.Id,
                Name = at.Name,
                Breeds = at.Breeds.Select(b => new BreedDto
                {
                    Id = b.Id,
                    Name = b.Name,
                    AnimalTypeId = b.AnimalTypeId
                }).ToList()
            })
            .ToListAsync();

        return Result<List<AnimalTypeDto>>.Success(types);
    }

    public async Task<Result<AnimalTypeDto>> CreateAnimalTypeAsync(Guid farmId, CreateAnimalTypeRequest request)
    {
        var animalType = new AnimalType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name,
            CreatedAt = DateTime.UtcNow
        };

        _context.AnimalTypes.Add(animalType);
        await _context.SaveChangesAsync();

        return Result<AnimalTypeDto>.Success(new AnimalTypeDto
        {
            Id = animalType.Id,
            Name = animalType.Name,
            Breeds = new List<BreedDto>()
        });
    }

    public async Task<Result> DeleteAnimalTypeAsync(Guid farmId, Guid id)
    {
        var animalType = await _context.AnimalTypes
            .FirstOrDefaultAsync(at => at.Id == id && at.FarmId == farmId);

        if (animalType == null)
            return Result.NotFound("Animal type not found");

        _context.AnimalTypes.Remove(animalType);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Breeds
    public async Task<Result<List<BreedDto>>> GetBreedsAsync(Guid farmId, Guid? animalTypeId = null)
    {
        var query = _context.Breeds
            .Where(b => b.AnimalType.FarmId == farmId);

        if (animalTypeId.HasValue)
            query = query.Where(b => b.AnimalTypeId == animalTypeId.Value);

        var breeds = await query
            .Select(b => new BreedDto
            {
                Id = b.Id,
                Name = b.Name,
                AnimalTypeId = b.AnimalTypeId,
                AverageGestationDays = b.AverageGestationDays
            })
            .ToListAsync();

        return Result<List<BreedDto>>.Success(breeds);
    }

    public async Task<Result<BreedDto>> CreateBreedAsync(Guid farmId, CreateBreedRequest request)
    {
        var animalType = await _context.AnimalTypes
            .FirstOrDefaultAsync(at => at.Id == request.AnimalTypeId && at.FarmId == farmId);

        if (animalType == null)
            return Result<BreedDto>.NotFound("Animal type not found");

        var breed = new Breed
        {
            Id = Guid.NewGuid(),
            AnimalTypeId = request.AnimalTypeId,
            Name = request.Name,
            AverageGestationDays = request.AverageGestationDays,
            CreatedAt = DateTime.UtcNow
        };

        _context.Breeds.Add(breed);
        await _context.SaveChangesAsync();

        return Result<BreedDto>.Success(new BreedDto
        {
            Id = breed.Id,
            Name = breed.Name,
            AnimalTypeId = breed.AnimalTypeId,
            AverageGestationDays = breed.AverageGestationDays
        });
    }

    public async Task<Result> DeleteBreedAsync(Guid farmId, Guid id)
    {
        var breed = await _context.Breeds
            .Include(b => b.AnimalType)
            .FirstOrDefaultAsync(b => b.Id == id && b.AnimalType.FarmId == farmId);

        if (breed == null)
            return Result.NotFound("Breed not found");

        _context.Breeds.Remove(breed);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Sex Options
    public async Task<Result<List<SexOptionDto>>> GetSexOptionsAsync(Guid farmId)
    {
        var options = await _context.SexOptions
            .Where(so => so.FarmId == farmId)
            .Select(so => new SexOptionDto { Id = so.Id, Value = so.Value })
            .ToListAsync();

        return Result<List<SexOptionDto>>.Success(options);
    }

    public async Task<Result<SexOptionDto>> CreateSexOptionAsync(Guid farmId, CreateSexOptionRequest request)
    {
        var sexOption = new SexOption
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Value = request.Value,
            CreatedAt = DateTime.UtcNow
        };

        _context.SexOptions.Add(sexOption);
        await _context.SaveChangesAsync();

        return Result<SexOptionDto>.Success(new SexOptionDto { Id = sexOption.Id, Value = sexOption.Value });
    }

    public async Task<Result> DeleteSexOptionAsync(Guid farmId, Guid id)
    {
        var sexOption = await _context.SexOptions
            .FirstOrDefaultAsync(so => so.Id == id && so.FarmId == farmId);

        if (sexOption == null)
            return Result.NotFound("Sex option not found");

        _context.SexOptions.Remove(sexOption);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Age Categories
    public async Task<Result<List<AgeCategoryDto>>> GetAgeCategoriesAsync(Guid farmId)
    {
        var categories = await _context.AgeCategories
            .Where(ac => ac.FarmId == farmId)
            .Select(ac => new AgeCategoryDto
            {
                Id = ac.Id,
                Name = ac.Name,
                MinDays = ac.MinDays,
                MaxDays = ac.MaxDays
            })
            .ToListAsync();

        return Result<List<AgeCategoryDto>>.Success(categories);
    }

    public async Task<Result<AgeCategoryDto>> CreateAgeCategoryAsync(Guid farmId, CreateAgeCategoryRequest request)
    {
        var category = new AgeCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name,
            MinDays = request.MinDays,
            MaxDays = request.MaxDays,
            CreatedAt = DateTime.UtcNow
        };

        _context.AgeCategories.Add(category);
        await _context.SaveChangesAsync();

        return Result<AgeCategoryDto>.Success(new AgeCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            MinDays = category.MinDays,
            MaxDays = category.MaxDays
        });
    }

    public async Task<Result> DeleteAgeCategoryAsync(Guid farmId, Guid id)
    {
        var category = await _context.AgeCategories
            .FirstOrDefaultAsync(ac => ac.Id == id && ac.FarmId == farmId);

        if (category == null)
            return Result.NotFound("Age category not found");

        _context.AgeCategories.Remove(category);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Animal Statuses
    public async Task<Result<List<AnimalStatusDto>>> GetAnimalStatusesAsync(Guid farmId)
    {
        var statuses = await _context.AnimalStatuses
            .Where(a => a.FarmId == farmId)
            .Select(a => new AnimalStatusDto { Id = a.Id, Name = a.Name, IsActive = a.IsActive, Category = a.Category, IsSystemDefined = a.IsSystemDefined })
            .ToListAsync();

        return Result<List<AnimalStatusDto>>.Success(statuses);
    }

    public async Task<Result<AnimalStatusDto>> CreateAnimalStatusAsync(Guid farmId, CreateAnimalStatusRequest request)
    {
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name,
            IsActive = request.IsActive,
            Category = request.Category,
            CreatedAt = DateTime.UtcNow
        };

        _context.AnimalStatuses.Add(status);
        await _context.SaveChangesAsync();

        return Result<AnimalStatusDto>.Success(new AnimalStatusDto
        {
            Id = status.Id,
            Name = status.Name,
            IsActive = status.IsActive,
            Category = status.Category
        });
    }

    public async Task<Result> DeleteAnimalStatusAsync(Guid farmId, Guid id)
    {
        var status = await _context.AnimalStatuses
            .FirstOrDefaultAsync(a => a.Id == id && a.FarmId == farmId);

        if (status == null)
            return Result.NotFound("Animal status not found");

        _context.AnimalStatuses.Remove(status);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Identification Types
    public async Task<Result<List<IdentificationTypeDto>>> GetIdentificationTypesAsync(Guid farmId)
    {
        var types = await _context.IdentificationTypes
            .Where(it => it.FarmId == farmId)
            .Select(it => new IdentificationTypeDto { Id = it.Id, Name = it.Name })
            .ToListAsync();

        return Result<List<IdentificationTypeDto>>.Success(types);
    }

    public async Task<Result<IdentificationTypeDto>> CreateIdentificationTypeAsync(Guid farmId, CreateIdentificationTypeRequest request)
    {
        var type = new IdentificationType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name,
            CreatedAt = DateTime.UtcNow
        };

        _context.IdentificationTypes.Add(type);
        await _context.SaveChangesAsync();

        return Result<IdentificationTypeDto>.Success(new IdentificationTypeDto { Id = type.Id, Name = type.Name });
    }

    public async Task<Result> DeleteIdentificationTypeAsync(Guid farmId, Guid id)
    {
        var type = await _context.IdentificationTypes
            .FirstOrDefaultAsync(it => it.Id == id && it.FarmId == farmId);

        if (type == null)
            return Result.NotFound("Identification type not found");

        _context.IdentificationTypes.Remove(type);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Location Types
    public async Task<Result<List<LocationTypeDto>>> GetLocationTypesAsync(Guid farmId)
    {
        var types = await _context.LocationTypes
            .Where(lt => lt.FarmId == farmId)
            .Select(lt => new LocationTypeDto { Id = lt.Id, Name = lt.Name })
            .ToListAsync();

        return Result<List<LocationTypeDto>>.Success(types);
    }

    public async Task<Result<LocationTypeDto>> CreateLocationTypeAsync(Guid farmId, CreateLocationTypeRequest request)
    {
        var type = new LocationType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name,
            CreatedAt = DateTime.UtcNow
        };

        _context.LocationTypes.Add(type);
        await _context.SaveChangesAsync();

        return Result<LocationTypeDto>.Success(new LocationTypeDto { Id = type.Id, Name = type.Name });
    }

    public async Task<Result> DeleteLocationTypeAsync(Guid farmId, Guid id)
    {
        var type = await _context.LocationTypes
            .FirstOrDefaultAsync(lt => lt.Id == id && lt.FarmId == farmId);

        if (type == null)
            return Result.NotFound("Location type not found");

        _context.LocationTypes.Remove(type);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Locations
    public async Task<Result<List<LocationDto>>> GetLocationsAsync(Guid farmId)
    {
        var allLocations = await _context.Locations
            .Include(l => l.LocationType)
            .Where(l => l.FarmId == farmId)
            .ToListAsync();

        var locationDict = allLocations.ToDictionary(l => l.Id);
        var rootLocations = allLocations.Where(l => l.ParentLocationId == null).ToList();

        List<LocationDto> BuildChildren(Location parent)
        {
            return allLocations
                .Where(l => l.ParentLocationId == parent.Id)
                .Select(c => new LocationDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    LocationTypeId = c.LocationTypeId,
                    LocationTypeName = c.LocationType.Name,
                    ParentLocationId = c.ParentLocationId,
                    ParentLocationName = parent.Name,
                    ChildLocations = BuildChildren(c)
                })
                .ToList();
        }

        var result = rootLocations.Select(l => new LocationDto
        {
            Id = l.Id,
            Name = l.Name,
            LocationTypeId = l.LocationTypeId,
            LocationTypeName = l.LocationType.Name,
            ParentLocationId = l.ParentLocationId,
            ChildLocations = BuildChildren(l)
        }).ToList();

        return Result<List<LocationDto>>.Success(result);
    }

    public async Task<Result<LocationDto>> CreateLocationAsync(Guid farmId, CreateLocationRequest request)
    {
        var locationType = await _context.LocationTypes
            .FirstOrDefaultAsync(lt => lt.Id == request.LocationTypeId && lt.FarmId == farmId);

        if (locationType == null)
            return Result<LocationDto>.NotFound("Location type not found");

        if (request.ParentLocationId.HasValue)
        {
            var parent = await _context.Locations
                .FirstOrDefaultAsync(l => l.Id == request.ParentLocationId.Value && l.FarmId == farmId);

            if (parent == null)
                return Result<LocationDto>.NotFound("Parent location not found");
        }

        var location = new Location
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = request.Name,
            LocationTypeId = request.LocationTypeId,
            ParentLocationId = request.ParentLocationId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        return Result<LocationDto>.Success(new LocationDto
        {
            Id = location.Id,
            Name = location.Name,
            LocationTypeId = location.LocationTypeId,
            LocationTypeName = locationType.Name,
            ParentLocationId = location.ParentLocationId
        });
    }

    public async Task<Result> DeleteLocationAsync(Guid farmId, Guid id)
    {
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.Id == id && l.FarmId == farmId);

        if (location == null)
            return Result.NotFound("Location not found");

        var hasChildren = await _context.Locations.AnyAsync(l => l.ParentLocationId == id);
        if (hasChildren)
            return Result.Conflict("Cannot delete location with children");

        _context.Locations.Remove(location);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Custom Fields
    public async Task<Result<List<CustomFieldDefinitionDto>>> GetCustomFieldDefinitionsAsync(Guid farmId)
    {
        var fields = await _context.CustomFieldDefinitions
            .Where(cf => cf.FarmId == farmId)
            .Select(cf => new CustomFieldDefinitionDto
            {
                Id = cf.Id,
                FieldName = cf.FieldName,
                FieldType = cf.FieldType,
                IsRequired = cf.IsRequired,
                Options = cf.Options != null ? JsonSerializer.Deserialize<List<string>>(cf.Options, new JsonSerializerOptions()) : null
            })
            .ToListAsync();

        return Result<List<CustomFieldDefinitionDto>>.Success(fields);
    }

    public async Task<Result<CustomFieldDefinitionDto>> CreateCustomFieldDefinitionAsync(Guid farmId, CreateCustomFieldDefinitionRequest request)
    {
        var field = new CustomFieldDefinition
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FieldName = request.FieldName,
            FieldType = request.FieldType,
            IsRequired = request.IsRequired,
            Options = request.Options != null ? JsonSerializer.Serialize(request.Options) : null,
            CreatedAt = DateTime.UtcNow
        };

        _context.CustomFieldDefinitions.Add(field);
        await _context.SaveChangesAsync();

        return Result<CustomFieldDefinitionDto>.Success(new CustomFieldDefinitionDto
        {
            Id = field.Id,
            FieldName = field.FieldName,
            FieldType = field.FieldType,
            IsRequired = field.IsRequired,
            Options = request.Options
        });
    }

    public async Task<Result> DeleteCustomFieldDefinitionAsync(Guid farmId, Guid id)
    {
        var field = await _context.CustomFieldDefinitions
            .FirstOrDefaultAsync(cf => cf.Id == id && cf.FarmId == farmId);

        if (field == null)
            return Result.NotFound("Custom field not found");

        _context.CustomFieldDefinitions.Remove(field);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Farm Configuration (key-value)
    public async Task<Result<List<FarmConfigurationDto>>> GetFarmConfigurationsAsync(Guid farmId)
    {
        var configs = await _context.FarmConfigurations
            .Where(fc => fc.FarmId == farmId)
            .Select(fc => new FarmConfigurationDto
            {
                Id = fc.Id,
                Key = fc.Key,
                Value = fc.Value,
                Category = fc.Category
            })
            .ToListAsync();

        return Result<List<FarmConfigurationDto>>.Success(configs);
    }

    public async Task<Result<FarmConfigurationDto>> UpsertFarmConfigurationAsync(Guid farmId, UpdateFarmConfigurationRequest request)
    {
        var existing = await _context.FarmConfigurations
            .FirstOrDefaultAsync(fc => fc.FarmId == farmId && fc.Key == request.Key);

        if (existing != null)
        {
            existing.Value = request.Value;
            existing.Category = request.Category;
        }
        else
        {
            existing = new FarmConfiguration
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Key = request.Key,
                Value = request.Value,
                Category = request.Category,
                CreatedAt = DateTime.UtcNow
            };
            _context.FarmConfigurations.Add(existing);
        }

        await _context.SaveChangesAsync();

        return Result<FarmConfigurationDto>.Success(new FarmConfigurationDto
        {
            Id = existing.Id,
            Key = existing.Key,
            Value = existing.Value,
            Category = existing.Category
        });
    }

    public async Task<Result> DeleteFarmConfigurationAsync(Guid farmId, string key)
    {
        var config = await _context.FarmConfigurations
            .FirstOrDefaultAsync(fc => fc.FarmId == farmId && fc.Key == key);

        if (config == null)
            return Result.NotFound("Configuration not found");

        _context.FarmConfigurations.Remove(config);
        await _context.SaveChangesAsync();

        return Result.Success();
    }
}
