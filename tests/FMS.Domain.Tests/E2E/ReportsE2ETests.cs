using System.Net;
using System.Net.Http.Json;
using FMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

public class ReportsE2ETests : IClassFixture<ReportsE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory { }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;    private ApiSeedData.SeedIds Seed => _factory.SeedData ?? throw new InvalidOperationException("Host not initialized");

    public ReportsE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public void Dispose() { }

    private HttpClient GetClient()
    {
        _ = _factory.Host; // ensure host is created and data seeded
        return _factory.CreateAuthenticatedClient(Seed.FarmId);
    }

    // ── GET /api/farm/{farmId}/reports/animals ────────────

    [Fact]
    public async Task AnimalReport_ReturnsOk_WithCorrectCounts()
    {
        var client = GetClient();

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/animals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<AnimalReportResponse>();
        Assert.NotNull(report);

        Assert.True(report.TotalCount >= 1);

        Assert.NotEmpty(report.ByType);
        Assert.Equal("Cattle", report.ByType[0].Name);

        Assert.NotEmpty(report.ByStatus);

        // Sold status = terminal → mortality
        Assert.True(report.MortalityCount >= 1);
    }

    [Fact]
    public async Task AnimalReport_WithDateRange_FiltersCorrectly()
    {
        var client = GetClient();

        var from = DateTime.UtcNow.AddDays(-10000).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(-9000).ToString("yyyy-MM-dd");

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/animals?from={from}&to={to}");
        var report = await response.Content.ReadFromJsonAsync<AnimalReportResponse>();

        Assert.NotNull(report);
        Assert.Equal(0, report.TotalCount);
    }

    [Fact]
    public async Task AnimalReport_IncludesGrowthTrend()
    {
        var client = GetClient();

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/animals");
        var report = await response.Content.ReadFromJsonAsync<AnimalReportResponse>();

        Assert.NotNull(report);
        Assert.NotEmpty(report.GrowthTrend);
        Assert.True(report.GrowthTrend[0].AvgWeight > 0);
    }

    [Fact]
    public async Task AnimalReport_DifferentFarm_ReturnsZero()
    {
        var otherFarmId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(otherFarmId);

        var response = await client.GetAsync($"/api/farm/{otherFarmId}/reports/animals");
        var report = await response.Content.ReadFromJsonAsync<AnimalReportResponse>();

        Assert.NotNull(report);
        Assert.Equal(0, report.TotalCount);
        Assert.Empty(report.ByType);
    }

    // ── GET /api/farm/{farmId}/reports/medical ────────────

    [Fact]
    public async Task MedicalReport_ReturnsOk_WithSeededData()
    {
        var client = GetClient();

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/medical");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<MedicalReportResponse>();
        Assert.NotNull(report);

        Assert.Equal(1, report.TotalCases);
        Assert.Equal(75m, report.TotalCost);

        Assert.NotEmpty(report.MonthlyTrend);
        Assert.Equal(1, report.MonthlyTrend[0].CaseCount);
    }

    [Fact]
    public async Task MedicalReport_IncludesByStatus()
    {
        var client = GetClient();

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/medical");
        var report = await response.Content.ReadFromJsonAsync<MedicalReportResponse>();

        Assert.NotNull(report);
        Assert.NotEmpty(report.ByStatus);
        Assert.Contains(report.ByStatus, s => s.Count >= 1);
    }

    // ── GET /api/farm/{farmId}/reports/vaccination ─────────

    [Fact]
    public async Task VaccinationReport_ReturnsOk_WithSeededData()
    {
        var client = GetClient();

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/vaccination");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<VaccinationReportResponse>();
        Assert.NotNull(report);

        Assert.Equal(1, report.TotalVaccinations);
        Assert.Equal(25m, report.TotalCost);

        Assert.NotEmpty(report.ByVaccine);
        Assert.Equal("FMD Vaccine", report.ByVaccine[0].VaccineName);
    }

    [Fact]
    public async Task VaccinationReport_WithDateRange_FiltersCorrectly()
    {
        var client = GetClient();

        var from = DateTime.UtcNow.AddDays(-10000).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddDays(-9000).ToString("yyyy-MM-dd");

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/vaccination?from={from}&to={to}");
        var report = await response.Content.ReadFromJsonAsync<VaccinationReportResponse>();

        Assert.NotNull(report);
        Assert.Equal(0, report.TotalVaccinations);
        Assert.Empty(report.ByVaccine);
    }

    // ── GET /api/farm/{farmId}/reports/employees ──────────

    [Fact]
    public async Task EmployeeReport_ReturnsOk_WithZeroEmployees()
    {
        var client = GetClient();

        var response = await client.GetAsync($"/api/farm/{Seed.FarmId}/reports/employees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<EmployeeReportResponse>();
        Assert.NotNull(report);

        Assert.Equal(0, report.TotalEmployees);
        Assert.Equal(0, report.ActiveEmployees);
        Assert.Equal(0, report.TotalPaid);
        Assert.Empty(report.ByMonth);
    }

    [Fact]
    public async Task EmployeeReport_DifferentFarm_ReturnsZero()
    {
        var otherFarmId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(otherFarmId);

        var response = await client.GetAsync($"/api/farm/{otherFarmId}/reports/employees");
        var report = await response.Content.ReadFromJsonAsync<EmployeeReportResponse>();

        Assert.NotNull(report);
        Assert.Equal(0, report.TotalEmployees);
    }

    // ── Auth tests ───────────────────────────────────────

    [Fact]
    public async Task AllEndpoints_ReturnUnauthorized_WithoutToken()
    {
        var client = new TestWebApplicationFactory().CreateClient(); // no auth
        var farmId = Guid.NewGuid();

        var endpoints = new[]
        {
            $"/api/farm/{farmId}/reports/animals",
            $"/api/farm/{farmId}/reports/medical",
            $"/api/farm/{farmId}/reports/vaccination",
            $"/api/farm/{farmId}/reports/employees",
        };

        foreach (var endpoint in endpoints)
        {
            var response = await client.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ── DTOs for deserialization ──────────────────────────

    private class AnimalReportResponse
    {
        public int TotalCount { get; set; }
        public List<CountItem> ByType { get; set; } = new();
        public List<CountItem> ByStatus { get; set; } = new();
        public List<GrowthTrendItem> GrowthTrend { get; set; } = new();
        public int MortalityCount { get; set; }
        public int TransferCount { get; set; }
    }

    private class CountItem
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    private class GrowthTrendItem
    {
        public string Month { get; set; } = "";
        public decimal? AvgWeight { get; set; }
        public int AnimalCount { get; set; }
    }

    private class MedicalReportResponse
    {
        public int TotalCases { get; set; }
        public decimal TotalCost { get; set; }
        public List<MonthlyTrendItem> MonthlyTrend { get; set; } = new();
        public List<ByVetItem> ByVet { get; set; } = new();
        public List<ByStatusItem> ByStatus { get; set; } = new();
    }

    private class MonthlyTrendItem
    {
        public string Month { get; set; } = "";
        public int CaseCount { get; set; }
        public decimal Cost { get; set; }
    }

    private class ByVetItem
    {
        public string VetName { get; set; } = "";
        public int CaseCount { get; set; }
        public decimal TotalCost { get; set; }
    }

    private class ByStatusItem
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
    }

    private class VaccinationReportResponse
    {
        public int TotalVaccinations { get; set; }
        public decimal TotalCost { get; set; }
        public int OverdueCount { get; set; }
        public int UpcomingCount { get; set; }
        public List<MonthlyTrendItem> MonthlyTrend { get; set; } = new();
        public List<ByVaccineItem> ByVaccine { get; set; } = new();
    }

    private class ByVaccineItem
    {
        public string VaccineName { get; set; } = "";
        public int Count { get; set; }
        public decimal TotalCost { get; set; }
    }

    private class EmployeeReportResponse
    {
        public int TotalEmployees { get; set; }
        public int ActiveEmployees { get; set; }
        public decimal TotalPaid { get; set; }
        public int PaymentCount { get; set; }
        public decimal ExpectedMonthlyPayroll { get; set; }
        public List<MonthlyPayrollItem> ByMonth { get; set; } = new();
        public List<ByDepartmentItem> ByDepartment { get; set; } = new();
    }

    private class MonthlyPayrollItem
    {
        public string Month { get; set; } = "";
        public decimal Amount { get; set; }
        public int PaymentCount { get; set; }
    }

    private class ByDepartmentItem
    {
        public string DepartmentName { get; set; } = "";
        public int EmployeeCount { get; set; }
        public decimal TotalPaid { get; set; }
    }
}
