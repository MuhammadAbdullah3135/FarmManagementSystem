using FMS.Infrastructure.Farm.Export;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// The completeness guard for the whole feature.
///
/// "Full-farm export" is the easiest claim in this subphase to let rot: a new table added
/// next month will not announce itself, and an archive missing one entity looks exactly
/// like a complete one to whoever downloads it. So completeness is measured against the
/// model rather than against a list someone remembered to update — every entity that
/// carries a <c>FarmId</c> must be either exported or explicitly excluded with a reason.
/// </summary>
public class FarmExportEntityCoverageTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IReadOnlyList<string> FarmScopedEntityNames()
    {
        using var db = CreateContext();

        return db.Model.GetEntityTypes()
            .Select(entityType => (entityType.ClrType, HasFarmId: entityType.FindProperty("FarmId") is not null))
            .Where(entity => entity.HasFarmId)
            .Select(entity => entity.ClrType.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlySet<string> ModelEntityNames()
    {
        using var db = CreateContext();

        return db.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EveryFarmScopedEntity_IsExportedOrExplicitlyExcluded()
    {
        var farmScoped = FarmScopedEntityNames();
        var accountedFor = FarmExportEntities.AccountedForEntityNames;

        var missing = farmScoped
            .Where(name => !accountedFor.Contains(name))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These entities carry a FarmId and are neither exported nor excluded, so a farm's "
                + $"archive would silently omit them: {string.Join(", ", missing)}. "
                + $"Add them to FarmExportEntities, or to Exclusions with a reason. "
                + $"({farmScoped.Count} farm-scoped entities, {accountedFor.Count} accounted for.)");
    }

    [Fact]
    public void EveryEntityIsAccountedForExactlyOnce()
    {
        var exported = FarmExportEntities.Tables
            .Select(table => table.EntityType.Name)
            .ToHashSet(StringComparer.Ordinal);

        var duplicated = FarmExportEntities.Tables
            .GroupBy(table => table.EntityType.Name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicated);

        var alsoExcluded = FarmExportEntities.Exclusions
            .Select(exclusion => exclusion.Entity)
            .Where(entity => exported.Contains(entity))
            .ToList();

        Assert.True(
            alsoExcluded.Count == 0,
            $"These entities are both exported and excluded, which would put them in the archive "
                + $"and claim they are not in it: {string.Join(", ", alsoExcluded)}");
    }

    [Fact]
    public void EveryExclusionAndTableNameRefersToARealEntityType()
    {
        var modelNames = ModelEntityNames();

        var staleExclusions = FarmExportEntities.Exclusions
            .Select(exclusion => exclusion.Entity)
            .Where(entity => !modelNames.Contains(entity))
            .ToList();

        Assert.True(
            staleExclusions.Count == 0,
            $"An exclusion for an entity that no longer exists would hide a real one: {string.Join(", ", staleExclusions)}");
    }

    [Fact]
    public void EveryExclusionStatesAReason()
    {
        foreach (var exclusion in FarmExportEntities.Exclusions)
        {
            Assert.True(
                exclusion.Reason.Length > 20,
                $"{exclusion.Entity} is excluded without a reason worth reading: '{exclusion.Reason}'");
        }
    }

    [Fact]
    public void ExclusionsDoNotCoverAnyTable()
    {
        var tableEntities = FarmExportEntities.Tables
            .Select(table => table.EntityType.Name)
            .ToHashSet(StringComparer.Ordinal);

        var overlap = FarmExportEntities.Exclusions
            .Select(exclusion => exclusion.Entity)
            .Where(entity => tableEntities.Contains(entity))
            .ToList();

        Assert.Empty(overlap);
    }

    [Fact]
    public void ExactlyTheSevenImportableEntitiesAreReimportable()
    {
        var reimportable = FarmExportEntities.Tables
            .Where(table => table.Reimportable)
            .Select(table => table.Entity)
            .ToList();

        Assert.Equal(
            new[]
            {
                "Animals", "Employees", "InventoryItems", "Suppliers", "Customers",
                "Expenses", "IncomeRecords"
            },
            reimportable);
    }

    [Fact]
    public void FileNamesAreUniqueAndCsv()
    {
        var names = FarmExportEntities.Tables
            .Select(table => table.FileName)
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.EndsWith(".csv", name, StringComparison.Ordinal));

        // No table may claim the manifest's own name, or it would be mistaken for the
        // machine-readable index of the archive.
        Assert.DoesNotContain(FarmExportAssembler.ManifestFileName, names);
    }

    [Fact]
    public void TheAccountsOwnCredentialsAreNeverExported()
    {
        var exported = FarmExportEntities.Tables
            .Select(table => table.EntityType.Name)
            .ToHashSet(StringComparer.Ordinal);

        // These three are the credentials: an archive that carried any of them would be a
        // reason to change the passwords it contains.
        Assert.DoesNotContain("RefreshToken", exported);
        Assert.DoesNotContain("PasswordResetToken", exported);
        Assert.DoesNotContain("ProcessedMutation", exported);
    }
}
