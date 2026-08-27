using FMS.Application.Common;
using FMS.Application.Farm;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Farm;

public class FarmService : IFarmService
{
    private readonly FmsDbContext _context;

    public FarmService(FmsDbContext context)
    {
        _context = context;
    }

    public async Task<Result<FarmDto>> CreateFarmAsync(Guid accountId, Guid userId, CreateFarmRequest request)
    {
        var farm = new Domain.Entities.Farm
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Name = request.Name,
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        _context.Farms.Add(farm);

        // Add user as Owner of the farm
        _context.UserFarms.Add(new UserFarm
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FarmId = farm.Id,
            Role = "Owner",
            CreatedAt = DateTime.UtcNow
        });

        // Seed system-defined animal statuses
        var now = DateTime.UtcNow;
        _context.AnimalStatuses.AddRange(
            new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                Name = "Active",
                IsActive = true,
                Category = AnimalStatusCategory.Active,
                IsSystemDefined = true,
                CreatedAt = now
            },
            new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                Name = "Pregnant",
                IsActive = true,
                Category = AnimalStatusCategory.Active,
                IsSystemDefined = true,
                CreatedAt = now
            },
            new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                Name = "Lactating",
                IsActive = true,
                Category = AnimalStatusCategory.Active,
                IsSystemDefined = true,
                CreatedAt = now
            },
            new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                Name = "Dry",
                IsActive = true,
                Category = AnimalStatusCategory.Active,
                IsSystemDefined = true,
                CreatedAt = now
            },
            new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                Name = "Sold",
                IsActive = true,
                Category = AnimalStatusCategory.Terminal,
                IsSystemDefined = true,
                CreatedAt = now
            },
            new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = farm.Id,
                Name = "Deceased",
                IsActive = true,
                Category = AnimalStatusCategory.Terminal,
                IsSystemDefined = true,
                CreatedAt = now
            }
        );

        await _context.SaveChangesAsync();

        return Result<FarmDto>.Success(MapToDto(farm, "Owner"));
    }

    public async Task<Result<FarmDto>> GetFarmAsync(Guid farmId, Guid userId)
    {
        var userFarm = await _context.UserFarms
            .Include(uf => uf.Farm)
            .FirstOrDefaultAsync(uf => uf.FarmId == farmId && uf.UserId == userId);

        if (userFarm == null)
            return Result<FarmDto>.NotFound("Farm not found or access denied");

        return Result<FarmDto>.Success(MapToDto(userFarm.Farm, userFarm.Role));
    }

    public async Task<Result<List<FarmDto>>> GetUserFarmsAsync(Guid accountId, Guid userId)
    {
        var farms = await _context.UserFarms
            .Include(uf => uf.Farm)
            .Where(uf => uf.Farm.AccountId == accountId && uf.UserId == userId)
            .Select(uf => MapToDto(uf.Farm, uf.Role))
            .ToListAsync();

        return Result<List<FarmDto>>.Success(farms);
    }

    public async Task<Result<FarmDto>> UpdateFarmAsync(Guid farmId, Guid userId, UpdateFarmRequest request)
    {
        var userFarm = await _context.UserFarms
            .Include(uf => uf.Farm)
            .FirstOrDefaultAsync(uf => uf.FarmId == farmId && uf.UserId == userId);

        if (userFarm == null)
            return Result<FarmDto>.NotFound("Farm not found or access denied");

        if (userFarm.Role != "Owner" && userFarm.Role != "Manager")
            return Result<FarmDto>.Unauthorized("Only owners and managers can update farm details");

        var farm = userFarm.Farm;
        farm.Name = request.Name;
        farm.Description = request.Description;
        farm.IsActive = request.IsActive;
        farm.ModifiedAt = DateTime.UtcNow;
        farm.ModifiedBy = userId;

        await _context.SaveChangesAsync();

        return Result<FarmDto>.Success(MapToDto(farm, userFarm.Role));
    }

    public async Task<Result> DeleteFarmAsync(Guid farmId, Guid userId)
    {
        var userFarm = await _context.UserFarms
            .Include(uf => uf.Farm)
            .FirstOrDefaultAsync(uf => uf.FarmId == farmId && uf.UserId == userId);

        if (userFarm == null)
            return Result.NotFound("Farm not found or access denied");

        if (userFarm.Role != "Owner")
            return Result.Unauthorized("Only owners can delete farms");

        // Soft delete
        userFarm.Farm.IsDeleted = true;
        userFarm.Farm.DeletedAt = DateTime.UtcNow;
        userFarm.Farm.DeletedBy = userId;

        await _context.SaveChangesAsync();

        return Result.Success();
    }

    private static FarmDto MapToDto(Domain.Entities.Farm farm, string? role)
    {
        return new FarmDto
        {
            Id = farm.Id,
            Name = farm.Name,
            Description = farm.Description,
            IsActive = farm.IsActive,
            CreatedAt = farm.CreatedAt,
            UserFarmRole = role
        };
    }
}
