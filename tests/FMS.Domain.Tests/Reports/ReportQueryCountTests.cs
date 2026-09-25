using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Application.Health;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Reports;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// Every report here used to ask the database one question per (schedule × animal) pair, so its
/// cost grew with the farm rather than staying put — and a green suite could not see it, because
/// the provider the rest of the suite runs on issues no commands to count.
///
/// <para>
/// These tests assert the number of round trips, not the elapsed time: the count is the property
/// that matters and it does not vary with the machine. Each is asserted at two farm sizes, so a
/// reintroduced per-animal query fails on the constant rather than passing because the fixture
/// was small.
/// </para>
///
/// <para>
/// Measured on a relational provider before the rewrite (SQLite, against this same fixture), as
/// the "before" these constants are the "after" of:
/// <list type="bullet">
/// <item><c>GetVaccinationReportAsync</c> — 53 at 25 animals, 803 at 400 (<c>3 + pairs</c>). Now 4.</item>
/// <item><c>ComputeVaccinationStatusesAsync</c> — 52 and 802 (<c>2 + pairs</c>). Now 3.</item>
/// <item><c>ComputeWeightCheckStatusesAsync</c> — 55 and 1,273 (<c>2 + pairs + due/overdue</c>). Now 4.</item>
/// <item><c>GenerateTasksAsync</c> — 5 at both sizes already; pinned so it stays that way.</item>
/// </list>
/// </para>
/// </summary>
public class ReportQueryCountTests
{
    private readonly ITestOutputHelper _output;

    public ReportQueryCountTests(ITestOutputHelper output) => _output = output;

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record Measured(int Readers, int Writers);

    /// <summary>Seeds a farm of the given size and hands back every service under test.</summary>
    private static async Task<(
        SqliteQueryCounter Counter,
        ReportPerfFixture Fixture,
        ReportService Reports,
        VaccineService Vaccines,
        WeightCheckScheduleService WeightChecks,
        FeedService Feed)> BuildAsync(int animalCount)
    {
        var counter = new SqliteQueryCounter();
        var fixture = await ReportPerfFixture.SeedAsync(counter.Context, animalCount);
        var user = new FixedCurrentUser();

        return (
            counter,
            fixture,
            new ReportService(counter.Context),
            new VaccineService(counter.Context, user),
            new WeightCheckScheduleService(counter.Context, user),
            new FeedService(counter.Context, user));
    }

    // ── the report that started this ─────────────────────────────

    [Theory]
    [InlineData(25)]
    [InlineData(400)]
    public async Task VaccinationReport_ReadsAConstantNumberOfTimes(int animalCount)
    {
        var built = await BuildAsync(animalCount);
        using var counter = built.Counter;

        var measured = await MeasureReportAsync(built);

        _output.WriteLine($"{animalCount} animals / {built.Fixture.VaccinationPairs} pairs -> {measured.Readers} reads");

        // Look at the farm's history once, its schedules once, its animals once, and the
        // latest vaccination per pair once. Nothing here scales with the pair count.
        Assert.Equal(4, measured.Readers);
        Assert.NotEmpty((await built.Reports.GetVaccinationReportAsync(built.Fixture.FarmId, null, null)).Value!.ByVaccine);
    }

    // ── the dashboard and the 15-minute dispatcher ───────────────

    [Theory]
    [InlineData(25)]
    [InlineData(400)]
    public async Task VaccinationStatus_ReadsAConstantNumberOfTimes(int animalCount)
    {
        var built = await BuildAsync(animalCount);
        using var counter = built.Counter;

        var readers = await counter.CountReadersAsync(
            () => built.Vaccines.GetVaccinationStatusAsync(built.Fixture.FarmId));

        _output.WriteLine($"{animalCount} animals / {built.Fixture.VaccinationPairs} pairs -> {readers} reads");

        Assert.Equal(3, readers);
    }

    // ── the costliest of the four, because it also writes ────────

    [Theory]
    [InlineData(25)]
    [InlineData(400)]
    public async Task WeightCheckStatus_ReadsAConstantNumberOfTimes_AndWritesTheSameTasksAsBefore(int animalCount)
    {
        var built = await BuildAsync(animalCount);
        using var counter = built.Counter;

        List<WeightCheckStatusDto>? entries = null;
        var (readers, writers) = await counter.CountAllAsync(async () =>
        {
            entries = (await built.WeightChecks.GetWeightCheckStatusAsync(built.Fixture.FarmId)).Value!;
        });

        var dueOrOverdue = entries!
            .Count(e => e.Status == WeightCheckStatusType.Due || e.Status == WeightCheckStatusType.Overdue);

        _output.WriteLine(
            $"{animalCount} animals / {built.Fixture.WeightCheckPairs} pairs -> {readers} reads, "
            + $"{writers} writes ({dueOrOverdue} entries were due or overdue)");

        // Schedules, animals, the latest weight per animal, the farm's open tasks.
        Assert.Equal(4, readers);

        // The write side is unchanged, and that is asserted rather than assumed: one task per
        // due-or-overdue entry. Two schedules matching one animal still stage one row each,
        // because the check it replaced could not see the tasks this same call had staged but
        // not yet saved either. Deduplicating would change how many rows a farm gets.
        Assert.Equal(dueOrOverdue, writers);
    }

    // ── the daily feeding-task job ───────────────────────────────

    [Theory]
    [InlineData(25)]
    [InlineData(400)]
    public async Task FeedTaskGeneration_ReadsAConstantNumberOfTimes(int animalCount)
    {
        var built = await BuildAsync(animalCount);
        using var counter = built.Counter;

        List<FeedingTaskDto>? created = null;
        var (readers, writers) = await counter.CountAllAsync(async () =>
        {
            created = (await built.Feed.GenerateTasksAsync(
                built.Fixture.FarmId,
                new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date })).Value!;
        });

        _output.WriteLine($"{animalCount} animals -> {readers} reads, {writers} writes");

        // Schedules, the day's existing tasks, the candidate animals, their latest weights
        // (the plan has a weight range), the plan items.
        Assert.Equal(5, readers);
        Assert.Single(created!);

        // The count the task carries still comes from the same predicate, applied in memory.
        Assert.True(created![0].TargetAnimalCount > 0,
            "the diet plan's weight range should match some of the seeded animals");
    }

    // ── the structural claim, stated once ────────────────────────

    [Fact]
    public async Task EveryRewrittenPath_CostsTheSameOnABigFarmAsOnASmallOne()
    {
        var small = await EveryPathAsync(25);
        var large = await EveryPathAsync(400);

        _output.WriteLine($"25 animals:  {small}");
        _output.WriteLine($"400 animals: {large}");

        // A sixteen-fold bigger farm, the same bill. This is the property the rewrite buys, and
        // the one a future per-animal query would break.
        Assert.Equal(small, large);
    }

    private async Task<string> EveryPathAsync(int animalCount)
    {
        var built = await BuildAsync(animalCount);
        using var counter = built.Counter;

        var report = await MeasureReportAsync(built);
        var status = await counter.CountReadersAsync(
            () => built.Vaccines.GetVaccinationStatusAsync(built.Fixture.FarmId));
        var weightChecks = await counter.CountAllAsync(
            () => built.WeightChecks.GetWeightCheckStatusAsync(built.Fixture.FarmId));
        var feed = await counter.CountAllAsync(
            () => built.Feed.GenerateTasksAsync(
                built.Fixture.FarmId, new GenerateFeedingTasksRequest { Date = DateTime.UtcNow.Date }));

        // The guard against the whole class of defect returning: no report may ask the database
        // once per animal.
        Assert.All(
            new[] { report.Readers, status, weightChecks.Readers, feed.Readers },
            readers => Assert.True(
                readers < animalCount,
                $"a path cost {readers} round trips for {animalCount} animals — that scales with the farm again"));

        return $"report={report.Readers} status={status} weightChecks={weightChecks.Readers} feed={feed.Readers}";
    }

    private static async Task<Measured> MeasureReportAsync(
        (SqliteQueryCounter Counter, ReportPerfFixture Fixture, ReportService Reports,
            VaccineService Vaccines, WeightCheckScheduleService WeightChecks, FeedService Feed) built)
    {
        await built.Counter.CountReadersAsync(
            () => built.Reports.GetVaccinationReportAsync(built.Fixture.FarmId, null, null));

        return new Measured(built.Counter.Readers, built.Counter.Writers);
    }
}
