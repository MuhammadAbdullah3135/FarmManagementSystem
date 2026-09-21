using System.Net;
using System.Net.Http.Json;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The cost-per-animal report over HTTP, through the real controller and the real
/// <c>FarmContextMiddleware</c> — because the route, the farm gate and the DI wiring are the
/// parts a service test cannot see.
///
/// The unit tests hand-compute the allocation; this one checks the report survives the trip
/// to a real host with real seed data, and that its reconciliation identities still hold on
/// data it did not control.
/// </summary>
public class CostReportE2ETests : IClassFixture<CostReportE2ETests.Factory>
{
    private readonly Factory _factory;

    public CostReportE2ETests(Factory factory) => _factory = factory;

    private Guid FarmId
    {
        get
        {
            _ = _factory.Host;
            return _factory.SeedFarmId;
        }
    }

    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        public Guid SeedFarmId { get; private set; }

        /// <summary>A farm the seeded test user has no membership in.</summary>
        public Guid ForeignFarmId { get; private set; }

        /// <summary>Feed, a location expense and a salary, so the report has all three scopes to show.</summary>
        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            SeedFarmId = seed.FarmId;

            ForeignFarmId = Guid.NewGuid();
            db.Farms.Add(new Farm { Id = ForeignFarmId, AccountId = seed.AccountId, Name = "Foreign Farm" });

            var fedAnimal = await db.Animals.FirstAsync(a => a.FarmId == seed.FarmId && a.TagNumber == "E2E-001");

            var feedType = new FeedType
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                Name = "Alfalfa",
                Category = FMS.Domain.Enums.FeedCategory.Forage,
                Unit = FMS.Domain.Enums.FeedUnit.Kilogram,
                CostPerUnit = 5m
            };
            db.FeedTypes.Add(feedType);

            // Stock bought well before the report range, so the purchase itself is not in range.
            db.FeedStockMovements.Add(new FeedStockMovement
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                FeedTypeId = feedType.Id,
                MovementType = FMS.Domain.Enums.StockMovementType.Purchase,
                Quantity = 100,
                UnitCost = 5m,
                TotalCost = 500m,
                MovementDate = DateTime.UtcNow.AddDays(-60)
            });

            // 2 kg at 5.00 = 10.00, against one animal.
            db.FeedRecords.Add(new FeedRecord
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                FeedTypeId = feedType.Id,
                AnimalId = fedAnimal.Id,
                Quantity = 2m,
                UnitCost = 5m,
                FedAt = DateTime.UtcNow.AddDays(-6)
            });

            // An expense against a pen, and a salary: a location pool and a farm pool.
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                ExpenseDate = DateTime.UtcNow.AddDays(-6),
                Amount = 240m,
                ExpenseCategoryId = seed.ExpenseCategoryId,
                PaymentMethodId = seed.PaymentMethodId,
                LocationId = seed.LocationId,
                Description = "Pen repairs"
            });

            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                FirstName = "Test",
                LastName = "Herder",
                SalaryType = FMS.Domain.Enums.SalaryType.Monthly,
                SalaryRate = 960m,
                HireDate = DateTime.UtcNow.AddYears(-1)
            };
            db.Employees.Add(employee);
            db.SalaryPayments.Add(new SalaryPayment
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                EmployeeId = employee.Id,
                Amount = 960m,
                PaymentDate = DateTime.UtcNow.AddDays(-4),
                SalaryType = FMS.Domain.Enums.SalaryType.Monthly
            });

            await db.SaveChangesAsync();
        }
    }

    private HttpClient Client(Guid farmId) => _factory.CreateAuthenticatedClient(farmId);

    private static string Day(int offset) => DateTime.UtcNow.AddDays(offset).ToString("yyyy-MM-dd");

    /// <summary>Twelve days, so the seeded expense at today-10 is in range and the vaccination at today-15 is not.</summary>
    private static string Range => $"?from={Day(-11)}&to={Day(0)}";

    [Fact]
    public async Task CostReport_ReturnsTheAllocationAndItsReconciliation()
    {
        var farmId = FarmId;
        var response = await Client(farmId).GetAsync($"/api/farm/{farmId}/reports/cost-per-animal{Range}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loaded = await response.Content.ReadFromJsonAsync<ReportShape>();
        Assert.NotNull(loaded);
        var report = loaded!;

        // Eight animals are in the seed, all present for the whole twelve days.
        Assert.Equal(8, report.Animals.Count);
        Assert.Equal(96, report.TotalAnimalDays);

        // Every allocated figure carries the arithmetic behind it.
        var allocated = report.Animals
            .SelectMany(row => row.Costs)
            .Where(component => component.Method == "farm-animal-days")
            .ToList();
        Assert.NotEmpty(allocated);
        Assert.All(allocated, component =>
        {
            Assert.Equal(96, component.PoolDays);
            Assert.Equal(12, component.AllocatedDays);
        });

        // The salary is 960 over 96 animal-days: 120 each.
        Assert.All(report.Animals, row =>
            Assert.Equal(120m, row.Costs.Single(component => component.Key == "labour.farm").Amount));

        // The feed record names one animal, at 2 kg × 5.00.
        var fed = Assert.Single(report.Animals, row => row.TagNumber == "E2E-001");
        Assert.Equal(10m, fed.Costs.Single(component => component.Key == "feed.direct").Amount);

        // The pen expense is 240, shared by the five animals in that pen.
        var barnAnimals = report.Animals.Where(row => row.TagNumber.StartsWith("E2E-00")).ToList();
        Assert.Equal(5, barnAnimals.Count);
        Assert.All(barnAnimals, row =>
            Assert.Equal(48m, row.Costs.Single(component => component.Key == "expense.location").Amount));

        // Cost is the sum of the row's own components, and the farm total is the sum of the rows.
        Assert.All(report.Animals, row => Assert.Equal(row.Costs.Sum(x => x.Amount), row.TotalCost));
        Assert.Equal(report.Animals.Sum(row => row.TotalCost), report.Farm.TotalCost);

        // Reconciliation: expenses 250 farm-wide + 240 pen = 490, all of it on a row.
        Assert.Equal(490m, report.Reconciliation.FarmExpensesTotal);
        Assert.Equal(250m, report.Reconciliation.ExpensesAllocatedFromFarmPool);
        Assert.Equal(240m, report.Reconciliation.ExpensesAllocatedFromLocations);
        Assert.Equal(0m, report.Reconciliation.ExpensesUnallocated);
        Assert.True(report.Reconciliation.ExpensesReconcile);

        // The seeded feed record is the only feed in range.
        Assert.Equal(10m, report.Reconciliation.FeedConsumedTotal);
        Assert.True(report.Reconciliation.FeedReconciles);

        // The seeded medical record (75, no expense behind it) is in this range, the vaccination is not.
        Assert.Equal(75m, report.Reconciliation.HealthRecordsTotal);
        Assert.Equal(75m, report.Reconciliation.HealthCostOutsideTheExpenseLedger);
        Assert.True(report.Reconciliation.HealthReconciles);

        Assert.Equal(960m, report.Reconciliation.LabourTotal);
        // Cost in this report with no expense row at all: the 960 salary and the 10 of feed.
        Assert.Equal(970m, report.Reconciliation.CostOutsideTheExpenseLedger);

        Assert.DoesNotContain(report.Warnings, warning => warning.Code == "rows.unassigned");

        // The rules travel with the report so the UI can explain itself from one source.
        Assert.Contains(report.Rules, rule => rule.Key == "direct");
        Assert.Contains(report.Rules, rule => rule.Key == "farm-animal-days");
    }

    [Fact]
    public async Task CostReport_HerdRows_AreTheSumOfTheirAnimals()
    {
        var farmId = FarmId;
        var response = await Client(farmId).GetAsync($"/api/farm/{farmId}/reports/cost-per-animal{Range}");
        var report = (await response.Content.ReadFromJsonAsync<ReportShape>())!;

        Assert.Equal(report.Animals.Sum(row => row.TotalCost), report.Herds.Sum(herd => herd.TotalCost));
        Assert.Equal(2, report.Herds.Count); // Main Barn, and the animals with no location.

        var barn = Assert.Single(report.Herds, herd => herd.LocationName == "Main Barn");
        Assert.Equal(5, barn.AnimalCount);
        Assert.Equal(60, barn.AnimalDays);
    }

    [Fact]
    public async Task CostReport_FlagsDataThatIsMissingRatherThanShowingZero()
    {
        var farmId = FarmId;
        var response = await Client(farmId).GetAsync($"/api/farm/{farmId}/reports/cost-per-animal{Range}");
        var report = (await response.Content.ReadFromJsonAsync<ReportShape>())!;

        // The sold animal in the seed has no status-change event, so its departure is unknown
        // and its share of every pool is overstated — said out loud, not guessed.
        var departure = Assert.Single(report.Warnings, warning => warning.Code == "animal.departure-unknown");
        Assert.Equal(1, departure.AffectedCount);

        // No feed and no salary would each read as a confident zero without these.
        Assert.DoesNotContain(report.Warnings, warning => warning.Code == "feed.not-recorded");
        Assert.DoesNotContain(report.Warnings, warning => warning.Code == "labour.not-recorded");
    }

    [Fact]
    public async Task CostReport_WithoutAFeedRecord_SaysFeedIsMissing()
    {
        var farmId = FarmId;
        // A window with the animals present but nothing recorded against them: every cost the
        // report cannot see must be named rather than read as a confident zero.
        var response = await Client(farmId)
            .GetAsync($"/api/farm/{farmId}/reports/cost-per-animal?from={Day(-200)}&to={Day(-150)}");
        var report = (await response.Content.ReadFromJsonAsync<ReportShape>())!;

        Assert.True(report.TotalAnimalDays > 0);
        Assert.Equal(0m, report.Farm.TotalCost);
        Assert.Contains(report.Warnings, warning => warning.Code == "feed.not-recorded");
        Assert.Contains(report.Warnings, warning => warning.Code == "labour.not-recorded");
    }

    [Fact]
    public async Task CostReport_ForAFarmTheUserIsNotAMemberOf_IsRejected()
    {
        _ = _factory.Host; // ensures the host is created and its seed data written
        var foreignFarmId = _factory.ForeignFarmId;

        var response = await Client(foreignFarmId)
            .GetAsync($"/api/farm/{foreignFarmId}/reports/cost-per-animal{Range}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CostReport_WithoutAToken_IsUnauthorized()
    {
        var client = new TestWebApplicationFactory().CreateClient();
        var farmId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/farm/{farmId}/reports/cost-per-animal");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CostReport_WithAnInvertedRange_IsAValidationError()
    {
        var farmId = FarmId;
        var response = await Client(farmId)
            .GetAsync($"/api/farm/{farmId}/reports/cost-per-animal?from={Day(0)}&to={Day(-11)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── response shapes ─────────────────────────────────────

    private class ReportShape
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int TotalAnimalDays { get; set; }
        public List<RowShape> Animals { get; set; } = new();
        public List<HerdShape> Herds { get; set; } = new();
        public TotalsShape Farm { get; set; } = new();
        public List<RuleShape> Rules { get; set; } = new();
        public List<WarningShape> Warnings { get; set; } = new();
        public ReconciliationShape Reconciliation { get; set; } = new();
    }

    private class RowShape
    {
        public Guid AnimalId { get; set; }
        public string TagNumber { get; set; } = "";
        public string? LocationName { get; set; }
        public int AnimalDays { get; set; }
        public List<ComponentShape> Costs { get; set; } = new();
        public List<ComponentShape> Revenue { get; set; } = new();
        public decimal TotalCost { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    private class ComponentShape
    {
        public string Key { get; set; } = "";
        public string Method { get; set; } = "";
        public decimal Amount { get; set; }
        public int? AllocatedDays { get; set; }
        public int? PoolDays { get; set; }
    }

    private class HerdShape
    {
        public string LocationName { get; set; } = "";
        public int AnimalCount { get; set; }
        public int AnimalDays { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    private class TotalsShape
    {
        public decimal TotalCost { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    private class RuleShape
    {
        public string Key { get; set; } = "";
        public string Description { get; set; } = "";
    }

    private class WarningShape
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public int AffectedCount { get; set; }
    }

    private class ReconciliationShape
    {
        public decimal FarmExpensesTotal { get; set; }
        public decimal ExpensesAttributedToAnimals { get; set; }
        public decimal ExpensesAllocatedFromLocations { get; set; }
        public decimal ExpensesAllocatedFromFarmPool { get; set; }
        public decimal ExpensesUnallocated { get; set; }
        public bool ExpensesReconcile { get; set; }
        public decimal FeedConsumedTotal { get; set; }
        public bool FeedReconciles { get; set; }
        public decimal HealthRecordsTotal { get; set; }
        public decimal HealthCostOutsideTheExpenseLedger { get; set; }
        public bool HealthReconciles { get; set; }
        public decimal LabourTotal { get; set; }
        public decimal CostOutsideTheExpenseLedger { get; set; }
    }
}
