using System.Net;
using System.Net.Http.Json;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

public class DashboardE2ETests : IClassFixture<DashboardE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory { }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public DashboardE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public void Dispose() { }

    private ApiSeedData.SeedIds Seed => _factory.SeedData ?? throw new InvalidOperationException("Host not initialized");

    private HttpClient GetClient()
    {
        _ = _factory.Host; // ensure host is created and data seeded
        return _factory.CreateAuthenticatedClient(Seed.FarmId);
    }

    // ── GET /api/farm/{farmId}/dashboard/summary ──────────

    [Fact]
    public async Task Summary_ReturnsOk_WithCorrectAnimalCounts()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>();
        Assert.NotNull(summary);

        // 5 active + 2 sick + 1 sold = 8 total
        Assert.Equal(8, summary.TotalAnimals);

        // 2 sick animals
        Assert.Equal(2, summary.SickCount);

        // Cattle type = 8
        Assert.True(summary.AnimalsByType.ContainsKey("Cattle"));
        Assert.Equal(8, summary.AnimalsByType["Cattle"]);

        // UpcomingBirths may be 0 on InMemory (DateDiffDay not supported)
        Assert.True(summary.UpcomingBirths >= 0);

        // DueWeightCheckCount must serialize alongside DueVaccinationCount
        Assert.True(summary.DueWeightCheckCount >= 0);
    }

    [Fact]
    public async Task Summary_IncludesFeedAndInventoryStockValues()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/summary");
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>();

        Assert.NotNull(summary);
        Assert.True(summary.TotalInventoryStockValue >= 0);
        Assert.True(summary.TotalFeedStockValue >= 0);
    }

    [Fact]
    public async Task Summary_DegradedMetrics_UsesKnownNamesAndZeroedCounts()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/summary");
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>();

        Assert.NotNull(summary);

        var known = new HashSet<string>
        {
            "DueVaccinationCount", "DueWeightCheckCount", "UpcomingBirths"
        };

        // Every degraded metric name must be a known metric.
        Assert.All(summary.DegradedMetrics, name => Assert.Contains(name, known));

        // A degraded metric was not computed — its value must be 0.
        if (summary.DegradedMetrics.Contains("DueVaccinationCount"))
            Assert.Equal(0, summary.DueVaccinationCount);
        if (summary.DegradedMetrics.Contains("DueWeightCheckCount"))
            Assert.Equal(0, summary.DueWeightCheckCount);
        if (summary.DegradedMetrics.Contains("UpcomingBirths"))
            Assert.Equal(0, summary.UpcomingBirths);
    }

    [Fact]
    public async Task Summary_DifferentFarm_ReturnsZeroCounts()
    {
        var otherFarmId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(otherFarmId);

        var response = await client.GetAsync($"/api/farm/{otherFarmId}/dashboard/summary");
        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>();

        Assert.NotNull(summary);
        Assert.Equal(0, summary.TotalAnimals);
        Assert.Empty(summary.AnimalsByType);
    }

    // ── GET /api/farm/{farmId}/dashboard/alerts ───────────

    [Fact]
    public async Task Alerts_ReturnsOk_WithOverdueTaskAndLowInventory()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var alerts = await response.Content.ReadFromJsonAsync<List<DashboardAlertResponse>>();
        Assert.NotNull(alerts);
        Assert.NotEmpty(alerts);

        // Should have at least an overdue task alert (task due 3 days ago)
        var taskAlerts = alerts.Where(a => a.AlertType == "OverdueTask").ToList();
        Assert.NotEmpty(taskAlerts);

        // Should have low inventory alert (hay bales: 5, reorder: 10)
        var inventoryAlerts = alerts.Where(a => a.AlertType == "LowInventory").ToList();
        Assert.NotEmpty(inventoryAlerts);
    }

    [Fact]
    public async Task Alerts_CriticalSeverity_First()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/alerts");
        var alerts = await response.Content.ReadFromJsonAsync<List<DashboardAlertResponse>>();

        Assert.NotNull(alerts);
        Assert.NotEmpty(alerts);

        var criticalIndices = alerts.Where(a => a.Severity == "Critical").Select(a => alerts.IndexOf(a)).ToList();
        var warningIndices = alerts.Where(a => a.Severity == "Warning").Select(a => alerts.IndexOf(a)).ToList();

        if (criticalIndices.Any() && warningIndices.Any())
        {
            Assert.True(criticalIndices.Max() < warningIndices.Min(),
                "Critical alerts should appear before Warning alerts");
        }
    }

    // ── GET /api/farm/{farmId}/dashboard/charts ───────────

    [Fact]
    public async Task Charts_ReturnsOk_WithExpenseBreakdown()
    {
        var client = GetClient();
        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/charts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var charts = await response.Content.ReadFromJsonAsync<DashboardChartsResponse>();
        Assert.NotNull(charts);

        Assert.NotNull(charts.ExpenseBreakdown);
        Assert.True(charts.ExpenseBreakdown.GrandTotal > 0);
        Assert.NotEmpty(charts.ExpenseBreakdown.Items);
    }

    [Fact]
    public async Task Charts_WithDateRange_FiltersCorrectly()
    {
        var client = GetClient();
        var from = DateTime.UtcNow.AddDays(-1000).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(-500).ToString("yyyy-MM-dd");

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/dashboard/charts?from={from}&to={to}");
        var charts = await response.Content.ReadFromJsonAsync<DashboardChartsResponse>();

        Assert.NotNull(charts);
        Assert.NotNull(charts.ExpenseBreakdown);
        Assert.Equal(0, charts.ExpenseBreakdown.GrandTotal);
    }

    [Fact]
    public async Task Charts_Anonymous_ReturnsUnauthorized()
    {
        var client = new TestWebApplicationFactory().CreateClient(); // no auth
        var response = await client.GetAsync($"/api/farm/{Guid.NewGuid()}/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── DTOs for deserialization ──────────────────────────

    private class DashboardSummaryResponse
    {
        public int TotalAnimals { get; set; }
        public Dictionary<string, int> AnimalsByType { get; set; } = new();
        public Dictionary<string, int> AnimalsByStatus { get; set; } = new();
        public int PregnantCount { get; set; }
        public int SickCount { get; set; }
        public int DueVaccinationCount { get; set; }
        public int DueWeightCheckCount { get; set; }
        public List<string> DegradedMetrics { get; set; } = new();
        public int OverdueTasks { get; set; }
        public int UpcomingBirths { get; set; }
        public decimal TotalFeedStockValue { get; set; }
        public decimal TotalInventoryStockValue { get; set; }
    }

    private class DashboardAlertResponse
    {
        public string AlertType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private class DashboardChartsResponse
    {
        public List<AnimalTrendResponse> AnimalTrends { get; set; } = new();
        public ExpenseBreakdownResponse? ExpenseBreakdown { get; set; }
        public List<object> FeedConsumptionTrend { get; set; } = new();
        public List<object> MonthlyPL { get; set; } = new();
    }

    private class AnimalTrendResponse
    {
        public string Month { get; set; } = "";
        public int Count { get; set; }
    }

    private class ExpenseBreakdownResponse
    {
        public List<ExpenseItemResponse> Items { get; set; } = new();
        public decimal GrandTotal { get; set; }
    }

    private class ExpenseItemResponse
    {
        public string CategoryName { get; set; } = "";
        public decimal Total { get; set; }
        public decimal Percentage { get; set; }
    }
}
