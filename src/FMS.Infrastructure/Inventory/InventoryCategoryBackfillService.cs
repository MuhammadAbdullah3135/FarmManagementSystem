using FMS.Application.Inventory;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Inventory;

/// <summary>
/// Creates one category per distinct, non-empty category string in each farm and
/// links the items that do not have a <c>CategoryId</c> yet. It never changes the
/// retained string, never overwrites an existing link, and leaves blank/null
/// categories null, so a second run is a no-op.
/// </summary>
public class InventoryCategoryBackfillService : IInventoryCategoryBackfillService
{
    private readonly FmsDbContext _context;

    public InventoryCategoryBackfillService(FmsDbContext context)
    {
        _context = context;
    }

    public async Task<InventoryCategoryBackfillReport> RunAsync(
        bool apply, CancellationToken cancellationToken = default)
    {
        var unlinkedItems = await _context.InventoryItems
            .Where(item => item.CategoryId == null
                && item.Category != null
                && item.Category.Trim() != string.Empty)
            .ToListAsync(cancellationToken);

        var blankFarmIds = await _context.InventoryItems
            .Where(item => item.CategoryId == null
                && (item.Category == null || item.Category.Trim() == string.Empty))
            .Select(item => item.FarmId)
            .ToListAsync(cancellationToken);

        var existingCategories = await _context.InventoryCategories
            .ToListAsync(cancellationToken);

        // Keyed by farm and the case-insensitive name. A category created earlier in
        // this run is also inserted here so multiple differently-cased strings in the
        // same farm all point at the one new row.
        var categoriesByKey = new Dictionary<(Guid FarmId, string NormalizedName), InventoryCategory>();
        foreach (var category in existingCategories)
        {
            var key = (category.FarmId, Normalize(category.Name));
            if (!categoriesByKey.ContainsKey(key))
                categoriesByKey[key] = category;
        }

        var report = new InventoryCategoryBackfillReport { Applied = apply };
        var reportsByFarm = new Dictionary<Guid, InventoryCategoryBackfillFarmReport>();

        InventoryCategoryBackfillFarmReport FarmReport(Guid farmId)
        {
            if (!reportsByFarm.TryGetValue(farmId, out var row))
            {
                row = new InventoryCategoryBackfillFarmReport { FarmId = farmId };
                reportsByFarm[farmId] = row;
            }
            return row;
        }

        foreach (var blankFarmId in blankFarmIds)
            FarmReport(blankFarmId).BlankCategoryItemsLeftNull++;

        foreach (var group in unlinkedItems.GroupBy(item => new
                 {
                     item.FarmId,
                     NormalizedName = Normalize(item.Category!)
                 }))
        {
            var farmReport = FarmReport(group.Key.FarmId);
            var categoryKey = (group.Key.FarmId, group.Key.NormalizedName);

            if (!categoriesByKey.TryGetValue(categoryKey, out var category))
            {
                // The proposal's MIN(trim(Category)): one deterministic spelling wins,
                // while lower(...) is what groups the case variants together.
                var name = group
                    .Select(item => item.Category!.Trim())
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value, StringComparer.Ordinal)
                    .First();

                category = new InventoryCategory
                {
                    Id = Guid.NewGuid(),
                    FarmId = group.Key.FarmId,
                    Name = name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                categoriesByKey[categoryKey] = category;
                if (apply)
                    _context.InventoryCategories.Add(category);

                report.CategoriesCreated++;
                farmReport.CategoriesCreated++;
            }

            foreach (var item in group)
            {
                report.ItemsLinked++;
                farmReport.ItemsLinked++;
                if (apply)
                    item.CategoryId = category.Id;
            }
        }

        if (apply)
            await _context.SaveChangesAsync(cancellationToken);

        report.Farms = reportsByFarm.Values
            .OrderBy(row => row.FarmId)
            .ToList();
        return report;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
