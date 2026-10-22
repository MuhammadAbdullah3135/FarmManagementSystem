using FMS.Application.Health;

namespace FMS.API;

/// <summary>
/// The <c>vets:backfill</c> management command. It is handled before the API host starts
/// so a dry run cannot trigger the automatic <c>MigrateAsync()</c> and role seeding.
/// Whether the run writes anything is decided entirely by <c>--apply</c>; the dry run is
/// read-only and rolled back by the service.
/// </summary>
public static class VetBackfillCommand
{
    public const string CommandName = "vets:backfill";

    public static bool Matches(string[] args) =>
        args.Length > 0 && args[0] == CommandName;

    /// <summary>
    /// Runs the command and writes its report to <paramref name="output"/>. Returns the
    /// process exit code (0 for both a successful dry run and a successful apply).
    /// </summary>
    public static async Task<int> RunAsync(
        IVetBackfillService backfill,
        IReadOnlyList<string> args,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var apply = args.Skip(1).Any(arg => arg == "--apply");

        var report = await backfill.RunAsync(apply, cancellationToken);

        await output.WriteLineAsync(apply
            ? "Vet backfill applied."
            : "Vet backfill dry run (no changes written).");
        await output.WriteLineAsync($"vets created: {report.VetsCreated}");
        await output.WriteLineAsync($"medical records linked: {report.MedicalRecordsLinked}");
        await output.WriteLineAsync($"vaccination records linked: {report.VaccinationRecordsLinked}");
        await output.WriteLineAsync($"breeding records linked: {report.BreedingRecordsLinked}");
        await output.WriteLineAsync($"birth records linked: {report.BirthRecordsLinked}");
        await output.WriteLineAsync($"blank vet names left null: {report.BlankVetNamesLeftNull}");
        foreach (var farm in report.Farms)
        {
            await output.WriteLineAsync(
                $"  farm {farm.FarmId}: vets={farm.VetsCreated}, " +
                $"medical={farm.MedicalRecordsLinked}, vaccination={farm.VaccinationRecordsLinked}, " +
                $"breeding={farm.BreedingRecordsLinked}, birth={farm.BirthRecordsLinked}, " +
                $"blank={farm.BlankVetNamesLeftNull}");
        }

        if (!apply)
            await output.WriteLineAsync("Re-run with --apply to write these changes.");

        return 0;
    }
}
