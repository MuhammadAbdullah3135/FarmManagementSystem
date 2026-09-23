using System.Net;
using System.Net.Http.Json;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Integration tests against a real PostgreSQL server (not the EF InMemory provider).
/// These exercise the dashboard metrics, the chart/report queries and the write paths that
/// InMemory cannot validate — it evaluates LINQ client-side and ignores provider type mapping,
/// so a query PostgreSQL refuses to translate and a DateTime kind it refuses to compare both
/// look healthy there. Nothing else in the suite can see that class of defect.
///
/// Connection: FMS_TEST_POSTGRES env var, falling back to
/// Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres
///
/// If no PostgreSQL server is reachable the tests SKIP with a clear message
/// (never fail) so the suite remains usable on machines without a database.
/// </summary>
public class DashboardPostgresIntegrationTests : IClassFixture<PostgresIntegrationFactory>
{
    private readonly PostgresIntegrationFactory _factory;

    public DashboardPostgresIntegrationTests(PostgresIntegrationFactory fixture)
    {
        _factory = fixture;
    }

    private HttpClient GetClient()
    {
        Skip.If(_factory.UnavailableReason is not null, _factory.UnavailableReason);
        _ = _factory.Host; // ensure host is created and data seeded
        return _factory.CreateAuthenticatedClient(_factory.SeedData.FarmId);
    }

    [SkippableFact]
    public async Task Summary_OnRealPostgres_ComputesUpcomingBirths()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{_factory.SeedData.FarmId}/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>();

        Assert.NotNull(summary);
        // Seed: 1 confirmed gestation record with ExpectedDeliveryDate = UtcNow + 10 days.
        Assert.Equal(1, summary.UpcomingBirths);
        // Real PostgreSQL must compute all metrics — nothing may be degraded.
        Assert.Empty(summary.DegradedMetrics);
    }

    [SkippableFact]
    public async Task Alerts_OnRealPostgres_IncludesDueBirthAlert()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{_factory.SeedData.FarmId}/dashboard/alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var alerts = await response.Content.ReadFromJsonAsync<List<DashboardAlertResponse>>();

        Assert.NotNull(alerts);
        Assert.Contains(alerts, a => a.AlertType == "DueBirth");
    }

    // ── Charts and reports ────────────────────────────────
    //
    // These two endpoints grouped by month inside the SQL projection, with the month label
    // formatted in the same expression. PostgreSQL cannot translate that, so both answered 500
    // in production — the dashboard's four charts all rendered "No data" — while the InMemory
    // provider the rest of the suite uses returned rows. A real database is the only thing that
    // can assert this, which is why CI runs this class against PostgreSQL.

    [SkippableFact]
    public async Task Charts_OnRealPostgres_ReturnsAnimalTrends()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{_factory.SeedData.FarmId}/dashboard/charts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var charts = await response.Content.ReadFromJsonAsync<DashboardChartsResponse>();

        Assert.NotNull(charts);
        // Seed: four animals created just now, so this month is a bucket with a count.
        Assert.NotEmpty(charts!.AnimalTrends);
        Assert.All(charts.AnimalTrends, point => Assert.Matches(@"^\d{4}-\d{2}$", point.Month));
        Assert.Contains(charts.AnimalTrends, point => point.Count > 0);
    }

    [SkippableFact]
    public async Task AnimalReport_OnRealPostgres_ReturnsTheGrowthTrend()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{_factory.SeedData.FarmId}/reports/animals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<AnimalReportResponse>();

        Assert.NotNull(report);
        // Seed: one weight record, 30 days old.
        Assert.NotEmpty(report!.GrowthTrend);
        Assert.All(report.GrowthTrend, point => Assert.Matches(@"^\d{4}-\d{2}$", point.Month));
        Assert.True(report.GrowthTrend[0].AnimalCount > 0);
    }

    // ── Date-only query parameters ────────────────────────
    //
    // Query-string binding yields DateTimeKind.Unspecified, which PostgreSQL rejects against a
    // timestamp-with-time-zone column: `?date=2026-09-22` used to answer 400 while the same
    // request with a `Z` suffix answered 200. Every one of these is a request the SPA makes, so
    // a 400 here is a page that renders empty in production.

    [SkippableFact]
    public async Task DateOnlyQueryValues_OnRealPostgres_AreAccepted()
    {
        var client = GetClient();
        var farmId = _factory.SeedData.FarmId;
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var year = DateTime.UtcNow.Year;

        var requests = new (string Description, string Url)[]
        {
            ("feed tasks for a date", $"/api/farm/{farmId}/feed/tasks?date={today}&page=1&pageSize=10"),
            ("farm tasks", $"/api/farm/{farmId}/tasks?page=1&pageSize=10"),
            ("monthly summary for a year", $"/api/farm/{farmId}/finance/reports/monthly-summary?year={year}"),
            ("expense breakdown for a range", $"/api/farm/{farmId}/finance/reports/expense-breakdown?from=2026-01-01&to=2026-12-31"),
            ("profit and loss for a range", $"/api/farm/{farmId}/finance/reports/profit-loss?from=2026-01-01&to=2026-12-31"),
            ("health costs by month", $"/api/farm/{farmId}/health/costs/by-month?year={year}"),
            ("attendance for a range", $"/api/farm/{farmId}/attendance?from=2026-01-01&to=2026-12-31"),
            ("animal report for a range", $"/api/farm/{farmId}/reports/animals?from=2026-01-01&to=2026-12-31"),
        };

        foreach (var (description, url) in requests)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{description} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [SkippableFact]
    public async Task MonthlySummary_OnRealPostgres_ReconcilesTheSeededExpenseIntoItsYear()
    {
        var client = GetClient();
        var now = DateTime.UtcNow;

        // The seeded expense is 10 days old, which can fall in the previous year when this runs
        // in early January, so both years are summed rather than assuming which one holds it.
        var expenses = 0m;
        foreach (var year in new[] { now.Year - 1, now.Year })
        {
            var response = await client.GetAsync(
                $"/api/farm/{_factory.SeedData.FarmId}/finance/reports/monthly-summary?year={year}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var months = await response.Content.ReadFromJsonAsync<List<MonthlySummaryItemResponse>>();

            Assert.NotNull(months);
            Assert.Equal(12, months!.Count);
            expenses += months.Sum(m => m.Expenses);
        }

        // 200 with twelve zeroed months would pass the status assertion while proving nothing:
        // the year boundary has to actually find the data.
        Assert.True(expenses >= 250m, $"expected the seeded 250 expense to be found, found {expenses}");
    }

    // ── Write paths ───────────────────────────────────────
    //
    // The SPA posts a date-only value for an expense ("expenseDate": "2026-09-22"), which is
    // the very shape PostgreSQL rejected on the read side. Against the deployed API this create
    // answered 500 (trace 0HNONSA8QMOC4:00000004) with the expense never written, so the whole
    // path is asserted here instead: create, update, delete, and the list back where it started.

    [SkippableFact]
    public async Task Expense_CanBeCreatedUpdatedAndDeleted_WithThePayloadTheClientSends()
    {
        var client = GetClient();
        var farmId = _factory.SeedData.FarmId;

        var expenseDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var before = await CountExpensesAsync(client, farmId);

        var created = await client.PostAsJsonAsync($"/api/farm/{farmId}/finance/expenses", new
        {
            expenseDate,
            amount = 42.5m,
            expenseCategoryId = _factory.SeedData.ExpenseCategoryId,
            paymentMethodId = _factory.SeedData.PaymentMethodId,
            description = "PostgreSQL integration check - removed by this test",
        });

        Assert.True(created.IsSuccessStatusCode,
            $"create answered {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        var expense = await created.Content.ReadFromJsonAsync<ExpenseResponse>();
        Assert.NotNull(expense);
        Assert.Equal(42.5m, expense!.Amount);
        Assert.Equal(before + 1, await CountExpensesAsync(client, farmId));

        try
        {
            var updated = await client.PutAsJsonAsync($"/api/farm/{farmId}/finance/expenses/{expense.Id}", new
            {
                expenseDate,
                amount = 55m,
                expenseCategoryId = _factory.SeedData.ExpenseCategoryId,
                paymentMethodId = _factory.SeedData.PaymentMethodId,
                description = "PostgreSQL integration check - updated",
            });

            Assert.True(updated.IsSuccessStatusCode,
                $"update answered {(int)updated.StatusCode}: {await updated.Content.ReadAsStringAsync()}");
            var after = await updated.Content.ReadFromJsonAsync<ExpenseResponse>();
            Assert.Equal(55m, after!.Amount);
        }
        finally
        {
            // In a finally block on purpose: this runs against a shared database, and a failed
            // assertion above must not leave a row behind for the next run to trip over.
            var deleted = await client.DeleteAsync($"/api/farm/{farmId}/finance/expenses/{expense.Id}");
            Assert.True(deleted.IsSuccessStatusCode,
                $"delete answered {(int)deleted.StatusCode}: {await deleted.Content.ReadAsStringAsync()}");
        }

        Assert.Equal(before, await CountExpensesAsync(client, farmId));
    }

    private static async Task<int> CountExpensesAsync(HttpClient client, Guid farmId)
    {
        // pageSize=100 is the service's cap; a farm with more expenses than that would need
        // paging here, and the assertion would say so rather than silently undercount.
        var page = await client.GetFromJsonAsync<ExpensePageResponse>(
            $"/api/farm/{farmId}/finance/expenses?page=1&pageSize=100");

        Assert.NotNull(page);
        Assert.True(page!.TotalCount <= page.Items.Count, "expense count exceeds one page of 100 - page through it before trusting this");
        return page.TotalCount;
    }

    // ── DTOs for deserialization ──────────────────────────

    private class ExpenseResponse
    {
        public Guid Id { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
    }

    private class ExpensePageResponse
    {
        public List<ExpenseResponse> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }

    private class DashboardChartsResponse
    {
        public List<AnimalTrendPointResponse> AnimalTrends { get; set; } = new();
    }

    private class AnimalTrendPointResponse
    {
        public string Month { get; set; } = "";
        public int Count { get; set; }
    }

    private class AnimalReportResponse
    {
        public List<AnimalGrowthTrendResponse> GrowthTrend { get; set; } = new();
    }

    private class AnimalGrowthTrendResponse
    {
        public string Month { get; set; } = "";
        public decimal? AvgWeight { get; set; }
        public int AnimalCount { get; set; }
    }

    private class MonthlySummaryItemResponse
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Income { get; set; }
        public decimal Expenses { get; set; }
        public decimal Net { get; set; }
    }

    private class DashboardSummaryResponse
    {
        public int UpcomingBirths { get; set; }
        public List<string> DegradedMetrics { get; set; } = new();
        public int DueVaccinationCount { get; set; }
        public int DueWeightCheckCount { get; set; }
    }

    private class DashboardAlertResponse
    {
        public string AlertType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
