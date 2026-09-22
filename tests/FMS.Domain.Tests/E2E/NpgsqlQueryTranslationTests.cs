using FMS.Infrastructure.Dashboard;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Guards the queries whose *translation* matters, without needing a database.
/// </summary>
/// <remarks>
/// The suite's InMemory provider evaluates LINQ client-side, so it happily runs expressions
/// PostgreSQL cannot translate — which is how a formatted interpolated string inside a
/// dashboard projection shipped and took the whole charts endpoint down with a 500 (and, with
/// it, left every chart on the dashboard reading "No data"). <c>ToQueryString()</c> compiles
/// the query against the real Npgsql provider and throws on an untranslatable expression
/// before any connection is opened, so this runs anywhere, including CI without a database.
/// </remarks>
public class NpgsqlQueryTranslationTests
{
    private static FmsDbContext CreateNpgsqlContext()
    {
        // Never connected to: only the provider's SQL generator is exercised here.
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=5432;Database=fms_translation_only;Username=none;Password=none")
            .Options;

        return new FmsDbContext(options);
    }

    [Fact]
    public void AnimalTrendBuckets_TranslateForNpgsql_WithTheAggregationLeftInSql()
    {
        using var db = CreateNpgsqlContext();
        var from = new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc);

        var sql = DashboardService.BuildAnimalTrendBuckets(db, Guid.NewGuid(), from, to).ToQueryString();

        // The counting belongs to the database, and the month comes back as its two parts so
        // the label can be formatted in memory.
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count(*)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("date_part", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnimalTrendBuckets_TranslateWithoutADateRange()
    {
        using var db = CreateNpgsqlContext();

        var sql = DashboardService.BuildAnimalTrendBuckets(db, Guid.NewGuid(), null, null).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nails down the exact expression that broke both endpoints: projecting a formatted
    /// month label is fine, but *ordering by it* in the same statement is not translatable.
    /// </summary>
    /// <remarks>
    /// This is the trap the two fixed methods sat in — <c>Select(...Month = $"{year}-{month:D2}")</c>
    /// followed by <c>OrderBy(t =&gt; t.Month)</c> — and the reason the label is now built in
    /// memory from two integer parts that SQL can group and order on. The assertions are
    /// deliberately about EF's capability, not our code: if a future provider version starts
    /// translating this, that is news worth a failing test rather than a silent assumption.
    /// </remarks>
    [Fact]
    public void OrderingByAComputedLabel_IsNotTranslatable_WhichIsWhyOrderingIsDoneInMemory()
    {
        using var db = CreateNpgsqlContext();
        var farmId = Guid.NewGuid();

        var labelOnly = db.Animals
            .Where(a => a.FarmId == farmId && !a.IsDeleted)
            .GroupBy(a => new { a.CreatedAt.Year, a.CreatedAt.Month })
            .Select(g => new { Month = $"{g.Key.Year}-{g.Key.Month:D2}", Count = g.Count() });

        // The label alone is translatable...
        Assert.NotNull(labelOnly.ToQueryString());

        // ...but ordering the grouped result by that label is not.
        Assert.Throws<InvalidOperationException>(() => labelOnly.OrderBy(t => t.Month).ToQueryString());
    }
}
