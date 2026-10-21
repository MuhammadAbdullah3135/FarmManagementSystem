using FMS.Application.Inventory;

namespace FMS.API;

/// <summary>
/// The <c>inventory:categories:backfill</c> management command. It is handled before the
/// API host starts so a dry run cannot trigger the automatic <c>MigrateAsync()</c> and role
/// seeding that would otherwise run first. Whether the run writes anything is decided
/// entirely by <c>--apply</c>; the dry run is read-only and rolled back by the service.
/// </summary>
public static class InventoryCategoryBackfillCommand
{
    public const string CommandName = "inventory:categories:backfill";

    public static bool Matches(string[] args) =>
        args.Length > 0 && args[0] == CommandName;

    /// <summary>
    /// Runs the command and writes its report to <paramref name="output"/>. Returns the
    /// process exit code (0 for both a successful dry run and a successful apply).
    /// </summary>
    public static async Task<int> RunAsync(
        IInventoryCategoryBackfillService backfill,
        IReadOnlyList<string> args,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var apply = args.Skip(1).Any(arg => arg == "--apply");

        var report = await backfill.RunAsync(apply, cancellationToken);

        await output.WriteLineAsync(apply
            ? "Inventory category backfill applied."
            : "Inventory category backfill dry run (no changes written).");
        await output.WriteLineAsync($"categories created: {report.CategoriesCreated}");
        await output.WriteLineAsync($"items linked: {report.ItemsLinked}");
        await output.WriteLineAsync($"blank-category items left null: {report.BlankCategoryItemsLeftNull}");
        foreach (var farm in report.Farms)
        {
            await output.WriteLineAsync(
                $"  farm {farm.FarmId}: categories={farm.CategoriesCreated}, " +
                $"linked={farm.ItemsLinked}, blank={farm.BlankCategoryItemsLeftNull}");
        }

        if (!apply)
            await output.WriteLineAsync("Re-run with --apply to write these changes.");

        return 0;
    }
}
