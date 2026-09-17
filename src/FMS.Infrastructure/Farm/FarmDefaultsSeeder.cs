using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Farm;

/// <summary>
/// Seeds the default lookup data every farm needs to be usable:
/// animal statuses, sex options, age categories, location types + a root
/// location, and animal types + their common breeds.
///
/// <see cref="EnsureFarmDefaultsAsync"/> is idempotent per category — a category
/// is only seeded when the farm has zero rows of it — so it can be called from
/// every farm-creation path and safely re-run as a backfill on login for farms
/// created before this seeder existed.
///
/// Concurrency: the lookup tables intentionally have no unique (FarmId, Name)
/// index, so two concurrent backfills for the same farm could both pass the
/// zero-rows check and duplicate rows. On relational providers the seeding runs
/// inside a transaction guarded by a Postgres advisory lock keyed on the farm
/// id, and existence is re-checked inside the lock. Non-relational providers
/// (unit-test InMemory) skip the transaction/lock entirely.
/// </summary>
public static class FarmDefaultsSeeder
{
    private const string AdvisoryLockCategory = "FarmDefaultsSeeder";

    private static readonly (string Name, (string Name, int GestationDays)[] Breeds)[] DefaultAnimalTypes =
    [
        ("Cattle", [("Sahiwal", 283), ("Holstein Friesian", 283)]),
        ("Buffalo", [("Murrah", 316)]),
        ("Goat", [("Beetal", 150), ("Boer", 150)]),
        ("Sheep", [("Kajli", 147)]),
        ("Poultry", [("Rhode Island Red", 21)]),
    ];

    private static readonly (string Name, AnimalStatusCategory Category)[] DefaultStatuses =
    [
        ("Active", AnimalStatusCategory.Active),
        ("Pregnant", AnimalStatusCategory.Active),
        ("Lactating", AnimalStatusCategory.Active),
        ("Dry", AnimalStatusCategory.Active),
        ("Sold", AnimalStatusCategory.Terminal),
        ("Deceased", AnimalStatusCategory.Terminal),
    ];

    private static readonly string[] DefaultSexOptions = ["Male", "Female"];

    private static readonly (string Name, int MinDays, int MaxDays)[] DefaultAgeCategories =
    [
        ("Calf", 0, 180),
        ("Young", 181, 365),
        ("Adult", 366, 99999),
    ];

    private static readonly string[] DefaultLocationTypes = ["Shed", "Barn", "Field", "Pen", "Paddock"];

    /// <summary>
    /// Ensures the farm has all default lookup data. Cheap no-op when nothing
    /// is missing; otherwise seeds the missing categories (inside a lock on
    /// relational providers so concurrent logins cannot double-seed).
    /// </summary>
    public static async Task EnsureFarmDefaultsAsync(FmsDbContext context, Guid farmId)
    {
        if (!await HasMissingDefaultsAsync(context, farmId))
            return;

        var now = DateTime.UtcNow;

        if (context.Database.IsRelational())
        {
            var strategy = context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                await TakeAdvisoryLockAsync(context, farmId);

                // Re-check inside the lock: a concurrent run may have just
                // seeded the same categories.
                if (await HasMissingDefaultsAsync(context, farmId))
                    await SeedMissingAsync(context, farmId, now);

                await transaction.CommitAsync();
            });
        }
        else
        {
            // Non-relational providers (EF InMemory in unit tests) do not
            // support transactions; no concurrency to guard against there.
            if (await HasMissingDefaultsAsync(context, farmId))
                await SeedMissingAsync(context, farmId, now);
        }
    }

    private static async Task TakeAdvisoryLockAsync(FmsDbContext context, Guid farmId)
    {
        if (!context.Database.IsNpgsql())
            return;

        // Two-key transaction-scoped advisory lock; the key pair is stable for
        // a given farm so every backfill path serializes on the same lock.
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext({0}), hashtext({1}));",
            farmId.ToString(),
            AdvisoryLockCategory);
    }

    private static async Task<bool> HasMissingDefaultsAsync(FmsDbContext context, Guid farmId)
    {
        if (!await context.AnimalStatuses.AnyAsync(s => s.FarmId == farmId))
            return true;
        if (!await context.SexOptions.AnyAsync(s => s.FarmId == farmId))
            return true;
        if (!await context.AgeCategories.AnyAsync(c => c.FarmId == farmId))
            return true;
        if (!await context.LocationTypes.AnyAsync(lt => lt.FarmId == farmId))
            return true;
        if (!await context.Locations.AnyAsync(l => l.FarmId == farmId))
            return true;

        var types = await context.AnimalTypes
            .Include(at => at.Breeds)
            .Where(at => at.FarmId == farmId)
            .ToListAsync();

        var typeNames = types.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A default-named animal type the farm does not have yet.
        if (DefaultAnimalTypes.Any(d => !typeNames.Contains(d.Name)))
            return true;

        // A farm that has animal types but never established any breeds.
        // (Only when the whole breeds category is empty, so deliberately
        // trimmed breed lists on individual types are not resurrected.)
        if (types.Count > 0 && types.All(t => t.Breeds.Count == 0) && !await context.Breeds.AnyAsync(b => b.AnimalType.FarmId == farmId))
            return true;

        return false;
    }

    private static async Task SeedMissingAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        if (!await context.AnimalStatuses.AnyAsync(s => s.FarmId == farmId))
        {
            context.AnimalStatuses.AddRange(DefaultStatuses.Select(d => new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = d.Name,
                IsActive = true,
                Category = d.Category,
                IsSystemDefined = true,
                CreatedAt = now,
            }));
        }

        if (!await context.SexOptions.AnyAsync(s => s.FarmId == farmId))
        {
            context.SexOptions.AddRange(DefaultSexOptions.Select(value => new SexOption
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Value = value,
                CreatedAt = now,
            }));
        }

        if (!await context.AgeCategories.AnyAsync(c => c.FarmId == farmId))
        {
            context.AgeCategories.AddRange(DefaultAgeCategories.Select(d => new AgeCategory
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = d.Name,
                MinDays = d.MinDays,
                MaxDays = d.MaxDays,
                CreatedAt = now,
            }));
        }

        var locationTypes = await context.LocationTypes
            .Where(lt => lt.FarmId == farmId)
            .ToListAsync();

        if (locationTypes.Count == 0)
        {
            locationTypes = DefaultLocationTypes.Select(name => new LocationType
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = name,
                CreatedAt = now,
            }).ToList();
            context.LocationTypes.AddRange(locationTypes);
        }

        if (!await context.Locations.AnyAsync(l => l.FarmId == farmId))
        {
            context.Locations.Add(new Location
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                Name = "Main Farm",
                LocationTypeId = locationTypes[0].Id,
                ParentLocationId = null,
                CreatedAt = now,
            });
        }

        var existingTypes = await context.AnimalTypes
            .Include(at => at.Breeds)
            .Where(at => at.FarmId == farmId)
            .ToListAsync();
        var existingNames = existingTypes.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var farmHasBreeds = await context.Breeds.AnyAsync(b => b.AnimalType.FarmId == farmId);

        foreach (var (typeName, breeds) in DefaultAnimalTypes)
        {
            var existing = existingTypes.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farmId, Name = typeName, CreatedAt = now };
                context.AnimalTypes.Add(type);
                context.Breeds.AddRange(breeds.Select(d => new Breed
                {
                    Id = Guid.NewGuid(),
                    AnimalTypeId = type.Id,
                    Name = d.Name,
                    AverageGestationDays = d.GestationDays,
                    CreatedAt = now,
                }));
            }
            else if (!farmHasBreeds && existing.Breeds.Count == 0 && breeds.Length > 0)
            {
                // Backfill breeds for a default-named type the farm created
                // itself but never added breeds to (e.g. via the inline
                // "Add type" quick-add, which cannot create breeds).
                context.Breeds.AddRange(breeds.Select(d => new Breed
                {
                    Id = Guid.NewGuid(),
                    AnimalTypeId = existing.Id,
                    Name = d.Name,
                    AverageGestationDays = d.GestationDays,
                    CreatedAt = now,
                }));
            }
        }

        await context.SaveChangesAsync();
    }
}
