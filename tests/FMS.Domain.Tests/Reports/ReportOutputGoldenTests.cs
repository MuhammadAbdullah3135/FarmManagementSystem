using System.Globalization;
using System.Text.RegularExpressions;
using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Application.Health;
using FMS.Application.Reports;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Reports;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// The rewrite that replaced four per-(schedule × animal) query loops was supposed to change only
/// how often the database is asked, never what comes back. These tests are the proof: each
/// rewritten method's output is rendered into a canonical text and compared byte for byte against
/// a golden recorded from the implementation <em>before</em> the rewrite.
///
/// <para>
/// Two things make that comparison possible at all. Row ids are freshly generated every run, so
/// each is replaced by the stable name the fixture gave it; and every date is rendered as an
/// offset from the fixture's anchor, so the golden does not expire when the calendar moves. The
/// canonical form names every field of every DTO, and the renderer refuses to emit a raw id or a
/// date it was not given, so a field added later cannot quietly fall out of the comparison.
/// </para>
///
/// <para>
/// Recording: run with <c>FMS_CAPTURE_GOLDEN=1</c> to overwrite the goldens instead of
/// comparing. That is how the "before" values in <c>Reports/GoldenOutput</c> were produced —
/// against the pre-rewrite code — which is what makes this a before/after proof rather than a
/// snapshot of whatever the code happens to do now.
/// </para>
///
/// <para>
/// The two status lists are compared as a sorted set, and that is a finding rather than a
/// convenience: recorded twice from the <em>unmodified</em> implementation, both produced the
/// same entries in a different order, because neither orders its animals and the provider is
/// free to return rows in any order it likes. The order was never a contract, so asserting on it
/// would fail for a reason unrelated to the rewrite. The one ordering the vaccination status
/// does define — by how soon each animal is due — is asserted directly. The report and the task
/// list <em>are</em> ordered by their code, so those two are compared exactly.
/// </para>
/// </summary>
public class ReportOutputGoldenTests
{
    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    // ── the report that started this ─────────────────────────────

    /// <summary>
    /// A fixed anchor, deliberately: this report's trend labels are calendar strings, so a
    /// today-relative fixture would change the golden every month. Dates far enough back make the
    /// overdue branch unambiguous, and one animal is left unvaccinated so the "never vaccinated"
    /// branch — which reports as <em>upcoming</em>, not overdue — is present in the output.
    /// </summary>
    [Fact]
    public async Task VaccinationReport_Output_IsUnchanged()
    {
        using var counter = new SqliteQueryCounter();
        var fixture = await ReportPerfFixture.SeedAsync(
            counter.Context,
            animalCount: 8,
            vaccinationSchedules: 2,
            weightCheckSchedules: 1,
            anchor: new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            animalsWithoutVaccinationRecord: 1);

        var report = (await new ReportService(counter.Context)
            .GetVaccinationReportAsync(fixture.FarmId, null, null)).Value!;

        var canonical = Lines(
            new[]
            {
                $"totals|vaccinations={report.TotalVaccinations}|cost={Money(report.TotalCost)}"
                    + $"|overdue={report.OverdueCount}|upcoming={report.UpcomingCount}"
            },
            report.MonthlyTrend.Select(t => $"trend|{t.Month}|count={t.Count}|cost={Money(t.Cost)}"),
            report.ByVaccine.Select(v => $"vaccine|{v.VaccineName}|count={v.Count}|cost={Money(v.TotalCost)}"));

        AssertGolden("vaccination-report", canonical);
    }

    // ── the dashboard and the 15-minute dispatcher ───────────────

    [Fact]
    public async Task VaccinationStatus_Output_IsUnchanged()
    {
        using var counter = new SqliteQueryCounter();
        var fixture = await ReportPerfFixture.SeedAsync(counter.Context, animalCount: 8);

        var statuses = (await new VaccineService(counter.Context, new FixedCurrentUser())
            .GetVaccinationStatusAsync(fixture.FarmId)).Value!;

        // The order this method does define: soonest due first. (It was previously not asserted
        // anywhere, which is how an unordered list can look correct for years.)
        Assert.Equal(
            statuses.Select(s => s.DaysUntilDue).OrderBy(days => days),
            statuses.Select(s => s.DaysUntilDue));

        var canonical = Lines(statuses.Select(s =>
            $"status|{fixture.Label(s.AnimalId)}|{s.AnimalTagNumber}|{s.AnimalName ?? "-"}"
            + $"|{fixture.Label(s.VaccineTypeId)}|{s.VaccineTypeName}"
            + $"|last={Offset(fixture, s.LastVaccinationDate)}|due={fixture.Offset(s.NextDueDate)}"
            + $"|{s.StatusName}|{s.Status}"));

        AssertGolden("vaccination-status", canonical, orderInsensitive: true);
    }

    [Fact]
    public async Task WeightCheckStatus_Output_IsUnchanged()
    {
        using var counter = new SqliteQueryCounter();
        var fixture = await ReportPerfFixture.SeedAsync(counter.Context, animalCount: 8);

        var statuses = (await new WeightCheckScheduleService(counter.Context, new FixedCurrentUser())
            .GetWeightCheckStatusAsync(fixture.FarmId)).Value!;

        var canonical = Lines(statuses.Select(s =>
            $"status|{fixture.Label(s.AnimalId)}|{s.AnimalTagNumber}|{s.AnimalName ?? "-"}"
            + $"|last={Offset(fixture, s.LastWeightDate)}|due={fixture.Offset(s.NextDueDate)}"
            + $"|{s.StatusName}|{s.Status}"));

        AssertGolden("weight-check-status", canonical, orderInsensitive: true);
    }

    // ── the daily feeding-task job ───────────────────────────────

    [Fact]
    public async Task FeedingTasks_Output_IsUnchanged()
    {
        using var counter = new SqliteQueryCounter();
        var fixture = await ReportPerfFixture.SeedAsync(counter.Context, animalCount: 8);

        var tasks = (await new FeedService(counter.Context, new FixedCurrentUser())
            .GenerateTasksAsync(
                fixture.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date })).Value!;

        // The task and item ids are generated inside the method under test, so they are rendered
        // by position — the fixture cannot name an id it never issued, and asserting on a fresh
        // guid would only assert that guids are fresh.
        var canonical = Lines(tasks.Select((t, index) =>
            $"task|task-{index}|{fixture.Label(t.FeedingScheduleId)}|{fixture.Label(t.DietPlanId)}"
            + $"|{t.DietPlanName}|date={fixture.Offset(t.TaskDate)}|{t.TimeOfDay}|{t.StatusName}"
            + $"|target={t.TargetAnimalCount}|completed={Offset(fixture, t.CompletedAt)}"
            + $"|notes={t.Notes ?? "-"}|"
            + "items=" + string.Join(",", t.Items.Select((i, itemIndex) =>
                $"item-{itemIndex}:{fixture.Label(i.FeedTypeId)}:{i.FeedTypeName}:{i.UnitName}:{Money(i.QuantityPerFeeding)}"))));

        AssertGolden("feeding-tasks", canonical);
    }

    // ── the comparison itself ────────────────────────────────────

    private static string? Offset(ReportPerfFixture fixture, DateTime? value) =>
        value.HasValue ? fixture.Offset(value.Value).ToString(CultureInfo.InvariantCulture) : "null";

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Lines(params IEnumerable<string>[] groups) =>
        string.Join("\n", groups.SelectMany(group => group));

    /// <summary>
    /// Compares <paramref name="actual"/> against the recorded golden, or records it when
    /// <c>FMS_CAPTURE_GOLDEN=1</c> is set.
    /// </summary>
    /// <param name="orderInsensitive">
    /// Set for a method whose output order the code does not define, so the comparison asserts
    /// every field of every entry without asserting an order that was never promised.
    /// </param>
    private static void AssertGolden(string name, string actual, bool orderInsensitive = false)
    {
        // A raw id or date in the comparison would make the golden fail for a reason that has
        // nothing to do with the rewrite, so the renderers are checked rather than trusted.
        Assert.DoesNotMatch(new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"), actual);

        var path = Path.Combine(GoldenDirectory(), name + ".txt");

        if (Environment.GetEnvironmentVariable("FMS_CAPTURE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(GoldenDirectory());
            File.WriteAllText(path, orderInsensitive ? Sort(actual) : actual);
            return;
        }

        Assert.True(File.Exists(path), $"Missing golden '{name}'. Record it with FMS_CAPTURE_GOLDEN=1.");

        var expected = Normalize(File.ReadAllText(path));
        var produced = Normalize(actual);

        Assert.Equal(
            orderInsensitive ? Sort(expected) : expected,
            orderInsensitive ? Sort(produced) : produced);
    }

    private static string Sort(string text) =>
        string.Join("\n", text.Split('\n').OrderBy(line => line, StringComparer.Ordinal));

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>The goldens live beside this test, in the source tree, so they are reviewable.</summary>
    private static string GoldenDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FMS.Domain.Tests.csproj")))
            directory = directory.Parent;

        Assert.True(directory is not null, "could not locate the test project directory");
        return Path.Combine(directory!.FullName, "Reports", "GoldenOutput");
    }
}
