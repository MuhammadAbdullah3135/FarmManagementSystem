using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Application.Reports;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// The numbers in the cost report, hand-computed.
///
/// These tests exist because this report's whole claim is that every figure is traceable:
/// either a record that named the animal, or <c>this animal's days ÷ the pool's days ×
/// the pool</c>. So the assertions are the arithmetic itself — 10, 20 and 30 animal-days
/// against a 600 pool must be 100, 200 and 300 — plus the identities that prove nothing
/// was lost, double-counted, or invented.
/// </summary>
public class CostAttributionServiceTests
{
    private static readonly DateTime RangeFrom = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RangeTo = new(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Every animal is acquired before the range, so its animal-days start on 1 June.</summary>
    private static readonly DateTime Acquisition = new(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private static FeedService CreateFeedService(FmsDbContext context) => new(context, new FixedCurrentUser());

    private static CostAttributionService CreateService(FmsDbContext context) => new(
        context,
        CreateFeedService(context),
        new HealthCostService(context),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<CostAttributionService>.Instance);

    private sealed class Seed
    {
        public Guid FarmId { get; init; }
        public Guid OtherFarmId { get; init; }
        public Guid Barn1 { get; init; }
        public Guid Barn2 { get; init; }
        public Guid ActiveStatus { get; init; }
        public Guid SoldStatus { get; init; }
        public Guid AlfalfaId { get; init; }
        public Guid SilageId { get; init; }
        public Guid AnimalTypeId { get; init; }
        public Guid SexOptionId { get; init; }
    }

    /// <summary>The reference farm: two pens, an active and a terminal status, two feed types.</summary>
    private static async Task<Seed> SeedFarmAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var otherFarm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Other" };

        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var active = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };
        var sold = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Sold", Category = AnimalStatusCategory.Terminal };

        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn" };
        var barn1 = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn 1", LocationTypeId = locationType.Id };
        var barn2 = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn 2", LocationTypeId = locationType.Id };

        var alfalfa = new FeedType
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Alfalfa", Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram, CostPerUnit = 5m
        };
        var silage = new FeedType
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Silage", Category = FeedCategory.Forage,
            Unit = FeedUnit.Kilogram, CostPerUnit = 2m
        };

        context.Farms.AddRange(farm, otherFarm);
        context.AnimalTypes.Add(animalType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.AddRange(active, sold);
        context.LocationTypes.Add(locationType);
        context.Locations.AddRange(barn1, barn2);
        context.FeedTypes.AddRange(alfalfa, silage);
        await context.SaveChangesAsync();

        return new Seed
        {
            FarmId = farm.Id,
            OtherFarmId = otherFarm.Id,
            Barn1 = barn1.Id,
            Barn2 = barn2.Id,
            ActiveStatus = active.Id,
            SoldStatus = sold.Id,
            AlfalfaId = alfalfa.Id,
            SilageId = silage.Id,
            AnimalTypeId = animalType.Id,
            SexOptionId = sex.Id
        };
    }

    private static async Task<Animal> AddAnimalAsync(
        FmsDbContext context, Seed seed, string tag, Guid? locationId = null, Guid? farmId = null)
    {
        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farmId ?? seed.FarmId,
            TagNumber = tag,
            AnimalTypeId = seed.AnimalTypeId,
            SexOptionId = seed.SexOptionId,
            AnimalStatusId = seed.ActiveStatus,
            LocationId = locationId ?? seed.Barn1,
            AcquisitionDate = Acquisition
        };
        context.Animals.Add(animal);
        await context.SaveChangesAsync();
        return animal;
    }

    /// <summary>
    /// Moves an animal to the terminal status the way the app does: the status changes and a
    /// timeline event records it. <paramref name="withRelatedId"/> false reproduces a row
    /// written before the related id was stored, which must fall back to the event title.
    /// </summary>
    private static async Task DepartAsync(
        FmsDbContext context, Seed seed, Animal animal, DateTime date, bool withRelatedId = true)
    {
        animal.AnimalStatusId = seed.SoldStatus;
        context.AnimalTimelineEvents.Add(new AnimalTimelineEvent
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = seed.FarmId,
            EventType = TimelineEventTypes.StatusChanged,
            Title = "Active → Sold",
            OccurredAt = date,
            CreatedAt = DateTime.UtcNow,
            RelatedEntityId = withRelatedId ? seed.SoldStatus : null,
            RelatedEntityType = withRelatedId ? TimelineRelatedEntityTypes.AnimalStatus : null
        });
        await context.SaveChangesAsync();
    }

    /// <summary>Stock bought before the range so feed can be recorded without the purchase itself being in range.</summary>
    private static async Task StockUpAsync(FmsDbContext context, Seed seed)
    {
        var feed = CreateFeedService(context);
        await feed.RecordStockMovementAsync(seed.FarmId, new RecordStockMovementRequest
        {
            FeedTypeId = seed.AlfalfaId, MovementType = StockMovementType.Purchase,
            Quantity = 1000, UnitCost = 5m, MovementDate = Acquisition
        });
        await feed.RecordStockMovementAsync(seed.FarmId, new RecordStockMovementRequest
        {
            FeedTypeId = seed.SilageId, MovementType = StockMovementType.Purchase,
            Quantity = 1000, UnitCost = 2m, MovementDate = Acquisition
        });
    }

    private static async Task RecordFeedAsync(
        FmsDbContext context, Seed seed, Guid feedTypeId, decimal quantity,
        Guid? animalId = null, Guid? locationId = null, DateTime? fedAt = null)
    {
        var feed = CreateFeedService(context);
        var result = await feed.CreateFeedRecordAsync(seed.FarmId, new CreateFeedRecordRequest
        {
            FeedTypeId = feedTypeId,
            AnimalId = animalId,
            LocationId = locationId,
            Quantity = quantity,
            FedAt = fedAt ?? new DateTime(2025, 6, 5, 8, 0, 0, DateTimeKind.Utc)
        });
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    private static void AddExpense(
        FmsDbContext context, Seed seed, decimal amount, DateTime date,
        Guid? animalId = null, Guid? locationId = null, Guid? id = null)
    {
        context.Expenses.Add(new Expense
        {
            Id = id ?? Guid.NewGuid(),
            FarmId = seed.FarmId,
            ExpenseDate = date,
            Amount = amount,
            ExpenseCategoryId = Guid.NewGuid(),
            PaymentMethodId = Guid.NewGuid(),
            AnimalId = animalId,
            LocationId = locationId
        });
    }

    private static void AddIncome(
        FmsDbContext context, Seed seed, decimal amount, DateTime date,
        Guid? animalId = null, Guid? locationId = null)
    {
        context.IncomeRecords.Add(new IncomeRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            IncomeDate = date,
            Amount = amount,
            IncomeCategoryId = Guid.NewGuid(),
            PaymentMethodId = Guid.NewGuid(),
            AnimalId = animalId,
            LocationId = locationId
        });
    }

    private static void AddSalary(FmsDbContext context, Seed seed, decimal amount, DateTime? date = null)
    {
        context.SalaryPayments.Add(new SalaryPayment
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            EmployeeId = Guid.NewGuid(),
            Amount = amount,
            PaymentDate = date ?? new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            SalaryType = SalaryType.Monthly
        });
    }

    private static void AddTransfer(
        FmsDbContext context, Seed seed, Animal animal, Guid? from, Guid to, DateTime date)
    {
        context.AnimalTransfers.Add(new AnimalTransfer
        {
            Id = Guid.NewGuid(),
            AnimalId = animal.Id,
            FarmId = seed.FarmId,
            FromLocationId = from,
            ToLocationId = to,
            TransferredAt = date,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static Task<Result<CostPerAnimalReportDto>> RunAsync(FmsDbContext context, Guid farmId) =>
        CreateService(context).GetCostPerAnimalReportAsync(farmId, new CostReportFilter
        {
            From = RangeFrom,
            To = RangeTo
        });

    private static AnimalCostRowDto Row(CostPerAnimalReportDto report, string tag) =>
        report.Animals.Single(row => row.TagNumber == tag);

    private static CostComponentDto Component(AnimalCostRowDto row, string key) =>
        row.Costs.Concat(row.Revenue).Single(component => component.Key == key);

    private static bool HasComponent(AnimalCostRowDto row, string key) =>
        row.Costs.Concat(row.Revenue).Any(component => component.Key == key);

    // ── the allocation basis itself ─────────────────────────

    /// <summary>
    /// 10 + 20 + 30 animal-days against a 600 pool is 100, 200 and 300. This is the
    /// arithmetic the report shows, verified by hand rather than by the same code that
    /// produced it.
    /// </summary>
    [Fact]
    public async Task GetReport_SplitsFarmPoolByAnimalDays()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var stays = await AddAnimalAsync(context, seed, "A-30");
        var leavesLate = await AddAnimalAsync(context, seed, "A-20");
        var leavesEarly = await AddAnimalAsync(context, seed, "A-10");

        await DepartAsync(context, seed, leavesLate, new DateTime(2025, 6, 20, 0, 0, 0, DateTimeKind.Utc));
        await DepartAsync(context, seed, leavesEarly, new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc));
        AddSalary(context, seed, 600m);
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        Assert.Equal(60, report.TotalAnimalDays);

        var row30 = Row(report, "A-30");
        var row20 = Row(report, "A-20");
        var row10 = Row(report, "A-10");

        Assert.Equal(30, row30.AnimalDays);
        Assert.Equal(20, row20.AnimalDays);
        Assert.Equal(10, row10.AnimalDays);

        Assert.Equal(300m, Component(row30, CostComponentKeys.LabourFarm).Amount);
        Assert.Equal(200m, Component(row20, CostComponentKeys.LabourFarm).Amount);
        Assert.Equal(100m, Component(row10, CostComponentKeys.LabourFarm).Amount);

        // Every allocated figure carries its own arithmetic: days ÷ pool-days × pool.
        var labour = Component(row10, CostComponentKeys.LabourFarm);
        Assert.Equal(10, labour.AllocatedDays);
        Assert.Equal(60, labour.PoolDays);
        Assert.Equal(600m, labour.PoolAmount);
        Assert.Equal(CostAllocationMethods.FarmAnimalDays, labour.Method);

        // …and no money is created or lost by the split.
        Assert.Equal(600m, report.Animals.Sum(row => Component(row, CostComponentKeys.LabourFarm).Amount));

        Assert.Equal(RangeFrom, row30.PresentFrom);
        Assert.Null(row30.PresentTo);
        Assert.Equal(new DateTime(2025, 6, 20, 0, 0, 0, DateTimeKind.Utc), row20.PresentTo);
        Assert.Equal(new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc), row10.PresentTo);

        Assert.Equal(0.5m, row30.ShareOfFarmDays);
        Assert.Equal(600m, report.Farm.TotalCost);
        Assert.Equal(10m, report.Farm.CostPerAnimalDay);

        Assert.Equal(stays.Id, row30.AnimalId);
        Assert.Equal(leavesLate.Id, row20.AnimalId);
        Assert.Equal(leavesEarly.Id, row10.AnimalId);
    }

    /// <summary>Splitting a pool must not lose or invent a cent.</summary>
    [Fact]
    public async Task GetReport_RepeatedPoolSharesAddUpToThePoolExactly()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        await AddAnimalAsync(context, seed, "A-1");
        await AddAnimalAsync(context, seed, "A-2");
        await AddAnimalAsync(context, seed, "A-3");
        AddSalary(context, seed, 100m);
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        var shares = report.Animals
            .Select(row => Component(row, CostComponentKeys.LabourFarm))
            .ToList();

        Assert.Equal(100m, shares.Sum(share => share.Amount));
        Assert.Equal(0.01m, shares.Sum(share => share.RoundingAdjustment));
        Assert.Single(shares, share => share.RoundingAdjustment != 0m);

        // Each share is still days ÷ pool-days × pool, to the cent.
        Assert.All(shares, share =>
        {
            Assert.Equal(30, share.AllocatedDays);
            Assert.Equal(90, share.PoolDays);
            Assert.Equal(100m, share.PoolAmount);
        });
    }

    // ── direct facts are never shared ───────────────────────

    [Fact]
    public async Task GetReport_FeedNamingAnAnimalIsThatAnimalsCostOnly()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await StockUpAsync(context, seed);

        var fed = await AddAnimalAsync(context, seed, "A-1");
        await AddAnimalAsync(context, seed, "A-2");
        await RecordFeedAsync(context, seed, seed.AlfalfaId, 4m, animalId: fed.Id);

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        var component = Component(Row(report, "A-1"), CostComponentKeys.FeedDirect);
        Assert.Equal(20m, component.Amount);
        Assert.Equal(CostAllocationMethods.Direct, component.Method);
        Assert.Null(component.PoolAmount);
        Assert.Null(component.AllocatedDays);
        Assert.Equal(1, component.RecordCount);

        Assert.False(HasComponent(Row(report, "A-2"), CostComponentKeys.FeedDirect));
        Assert.Equal(0m, Row(report, "A-2").TotalCost);

        Assert.Equal(20m, report.Reconciliation.FeedConsumedTotal);
        Assert.Equal(20m, report.Reconciliation.FeedAttributedToAnimals);
        Assert.True(report.Reconciliation.FeedReconciles);
    }

    // ── location pools ──────────────────────────────────────

    [Fact]
    public async Task GetReport_SharesAPensFeedByDaysSpentInThatPen()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await StockUpAsync(context, seed);

        await AddAnimalAsync(context, seed, "A-1");
        await AddAnimalAsync(context, seed, "A-2");
        // 120 kg at 5.00 = a 600 pool shared over 60 animal-days in the pen.
        await RecordFeedAsync(context, seed, seed.AlfalfaId, 120m, locationId: seed.Barn1);

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        var each = report.Animals.Select(row => Component(row, CostComponentKeys.FeedLocation)).ToList();
        Assert.Equal(2, each.Count);
        Assert.All(each, component =>
        {
            Assert.Equal(300m, component.Amount);
            Assert.Equal(30, component.AllocatedDays);
            Assert.Equal(60, component.PoolDays);
            Assert.Equal(600m, component.PoolAmount);
            Assert.Equal(CostAllocationMethods.LocationAnimalDays, component.Method);
        });

        Assert.Equal(300m, Row(report, "A-1").TotalCost);
    }

    /// <summary>
    /// An animal that moves mid-range carries the right share of <b>each</b> pen, not all of
    /// its current one: 15 days of Barn 1 and 15 of Barn 2.
    /// </summary>
    [Fact]
    public async Task GetReport_AnimalThatMovedCarriesEachPensShare()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await StockUpAsync(context, seed);

        var mover = await AddAnimalAsync(context, seed, "A-MOVE");
        await AddAnimalAsync(context, seed, "A-STAY");

        AddTransfer(context, seed, mover, seed.Barn1, seed.Barn2,
            new DateTime(2025, 6, 16, 0, 0, 0, DateTimeKind.Utc));
        await context.SaveChangesAsync();
        mover.LocationId = seed.Barn2;
        await context.SaveChangesAsync();

        // Barn 1: 600 over 45 days (15 from the mover, 30 from the stayer). Barn 2: 300 over the mover's 15.
        await RecordFeedAsync(context, seed, seed.AlfalfaId, 120m, locationId: seed.Barn1);
        await RecordFeedAsync(context, seed, seed.SilageId, 150m, locationId: seed.Barn2);

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        var moverRow = Row(report, "A-MOVE");
        var stayerRow = Row(report, "A-STAY");

        var barn1 = moverRow.Costs.Single(component => component.Key == CostComponentKeys.FeedLocation
            && component.PoolAmount == 600m);
        var barn2 = moverRow.Costs.Single(component => component.Key == CostComponentKeys.FeedLocation
            && component.PoolAmount == 300m);

        Assert.Equal(200m, barn1.Amount);
        Assert.Equal(15, barn1.AllocatedDays);
        Assert.Equal(45, barn1.PoolDays);

        Assert.Equal(300m, barn2.Amount);
        Assert.Equal(15, barn2.AllocatedDays);
        Assert.Equal(15, barn2.PoolDays);

        Assert.Equal(500m, moverRow.TotalCost);
        Assert.Equal(400m, stayerRow.TotalCost);
        Assert.Equal(900m, report.Farm.TotalCost);

        // The herd table groups on where the animal is now.
        Assert.Equal(seed.Barn2, moverRow.LocationId);
        Assert.Equal("Barn 2", moverRow.LocationName);
    }

    /// <summary>A pool whose animals were never recorded in it is reported, not spread.</summary>
    [Fact]
    public async Task GetReport_PoolWithNoAnimalInItIsReportedAsUnallocated()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await StockUpAsync(context, seed);

        await AddAnimalAsync(context, seed, "A-1");
        await AddAnimalAsync(context, seed, "A-2");
        await RecordFeedAsync(context, seed, seed.AlfalfaId, 120m, locationId: seed.Barn2);

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        Assert.All(report.Animals, row => Assert.False(HasComponent(row, CostComponentKeys.FeedLocation)));
        Assert.Equal(0m, report.Farm.TotalCost);

        Assert.Equal(600m, report.Reconciliation.FeedUnallocated);
        Assert.True(report.Reconciliation.FeedReconciles);

        var warning = Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.PoolUnallocated);
        Assert.Equal(600m, warning.Amount);
    }

    /// <summary>Nothing present in the range is a division by zero avoided, not a crash or a silent zero.</summary>
    [Fact]
    public async Task GetReport_WithNoAnimalsPresentReportsEveryPoolAsUnallocated()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        AddSalary(context, seed, 600m);
        AddExpense(context, seed, 100m, new DateTime(2025, 6, 3, 0, 0, 0, DateTimeKind.Utc));
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        Assert.Empty(report.Animals);
        Assert.Empty(report.Herds);
        Assert.Equal(0, report.TotalAnimalDays);
        Assert.Equal(0m, report.Farm.TotalCost);

        var warning = Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.PoolUnallocated);
        Assert.Equal(700m, warning.Amount);
    }

    // ── departures ──────────────────────────────────────────

    /// <summary>
    /// Rows written before the status id was stored fall back to the event's own title, so
    /// existing farms' history is still read correctly.
    /// </summary>
    [Fact]
    public async Task GetReport_ReadsDepartureFromTheEventTitleWhenThereIsNoRelatedId()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var animal = await AddAnimalAsync(context, seed, "A-LEGACY");
        await DepartAsync(context, seed, animal, new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Utc),
            withRelatedId: false);

        var report = (await RunAsync(context, seed.FarmId)).Value!;
        var row = Row(report, "A-LEGACY");

        Assert.Equal(11, row.AnimalDays);
        Assert.Equal(new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Utc), row.PresentTo);
        Assert.DoesNotContain(CostWarningCodes.DepartureUnknown, row.Warnings);
    }

    /// <summary>
    /// An animal marked terminal with no readable departure date is counted as present and
    /// said so — overstating its share is visible, where guessing a date would not be.
    /// </summary>
    [Fact]
    public async Task GetReport_TerminalStatusWithNoDepartureDateIsFlaggedNotGuessed()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var animal = await AddAnimalAsync(context, seed, "A-UNKNOWN");
        animal.AnimalStatusId = seed.SoldStatus;
        await context.SaveChangesAsync();
        AddSalary(context, seed, 90m);
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;
        var row = Row(report, "A-UNKNOWN");

        Assert.Equal(30, row.AnimalDays);
        Assert.Contains(CostWarningCodes.DepartureUnknown, row.Warnings);

        var warning = Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.DepartureUnknown);
        Assert.Equal(1, warning.AffectedCount);

        // The inflated animal-days are visible in the allocation the row shows.
        Assert.Equal(90m, Component(row, CostComponentKeys.LabourFarm).Amount);
        Assert.Equal(30, Component(row, CostComponentKeys.LabourFarm).PoolDays);
    }

    /// <summary>An animal with neither an acquisition date nor a date of birth is counted from the range start, and flagged.</summary>
    [Fact]
    public async Task GetReport_AnimalWithNoEntryDateIsCountedFromTheRangeStartAndFlagged()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var animal = await AddAnimalAsync(context, seed, "A-NODATE");
        animal.AcquisitionDate = null;
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;
        var row = Row(report, "A-NODATE");

        Assert.Equal(30, row.AnimalDays);
        Assert.Contains(CostWarningCodes.PresenceStartUnknown, row.Warnings);
        Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.PresenceStartUnknown);
    }

    /// <summary>An animal sold before the range but earning in it keeps its row — and its money.</summary>
    [Fact]
    public async Task GetReport_AnimalWithMoneyInTheRangeKeepsItsRowEvenWithNoDays()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var sold = await AddAnimalAsync(context, seed, "A-SOLD");
        await DepartAsync(context, seed, sold, new DateTime(2025, 5, 20, 0, 0, 0, DateTimeKind.Utc));
        AddIncome(context, seed, 400m, new DateTime(2025, 6, 5, 0, 0, 0, DateTimeKind.Utc), animalId: sold.Id);
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        var row = Row(report, "A-SOLD");
        Assert.Equal(0, row.AnimalDays);
        Assert.Equal(400m, row.TotalRevenue);
        Assert.Equal(400m, report.Farm.TotalRevenue);

        // With no days in the range it takes no share of shared cost.
        Assert.False(HasComponent(row, CostComponentKeys.LabourFarm));
    }

    // ── health money is counted once ────────────────────────

    /// <summary>
    /// The health module writes a cost on the record <i>and</i> an expense row without an
    /// animal. Counting both would double every vet bill; exactly one is counted, and the
    /// reconciliation names which.
    /// </summary>
    [Fact]
    public async Task GetReport_HealthCostWithItsExpenseIsCountedExactlyOnce()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var animal = await AddAnimalAsync(context, seed, "A-SICK");
        var expenseId = Guid.NewGuid();
        AddExpense(context, seed, 250m, new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc), id: expenseId);
        AddExpense(context, seed, 75m, new DateTime(2025, 6, 12, 0, 0, 0, DateTimeKind.Utc), animalId: animal.Id);
        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            AnimalId = animal.Id,
            Symptoms = "Lameness",
            Cost = 250m,
            DateRecorded = new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            ExpenseId = expenseId
        });
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;
        var row = Row(report, "A-SICK");

        Assert.Equal(250m, Component(row, CostComponentKeys.HealthRecords).Amount);
        Assert.Equal(75m, Component(row, CostComponentKeys.ExpenseDirect).Amount);
        Assert.Equal(325m, row.TotalCost);

        Assert.Equal(325m, report.Reconciliation.FarmExpensesTotal);
        Assert.Equal(250m, report.Reconciliation.HealthLinkedExpenses);
        Assert.Equal(75m, report.Reconciliation.ExpensesAttributedToAnimals);
        Assert.Equal(0m, report.Reconciliation.ExpensesAllocatedFromFarmPool);
        Assert.Equal(0m, report.Reconciliation.HealthCostOutsideTheExpenseLedger);
        Assert.True(report.Reconciliation.ExpensesReconcile);
        Assert.True(report.Reconciliation.HealthReconciles);
        Assert.DoesNotContain(report.Warnings, item => item.Code == CostWarningCodes.RowsUnassigned);
    }

    /// <summary>
    /// An animal with no name is normal — the name is optional — and must produce a row, not
    /// an exception from the health figures this report borrows.
    /// </summary>
    [Fact]
    public async Task GetReport_HandlesAnAnimalWithNoName()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var animal = await AddAnimalAsync(context, seed, "A-NONAME");
        animal.Name = null;
        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            AnimalId = animal.Id,
            Symptoms = "Checkup",
            Cost = 40m,
            DateRecorded = new DateTime(2025, 6, 9, 0, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;
        var row = Row(report, "A-NONAME");

        Assert.Null(row.Name);
        Assert.Equal(40m, Component(row, CostComponentKeys.HealthRecords).Amount);
    }

    // ── the arithmetic can be checked against the other reports ──

    /// <summary>
    /// Every source total in the range lands in exactly one bucket: an animal row, or
    /// unallocated. This is the report's central identity, asserted numerically against the
    /// feed and health services' own figures rather than re-derived here.
    /// </summary>
    [Fact]
    public async Task GetReport_ReconciliationTiesBackToTheSourceReports()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await StockUpAsync(context, seed);

        var animal1 = await AddAnimalAsync(context, seed, "A-1");
        var animal2 = await AddAnimalAsync(context, seed, "A-2");

        await RecordFeedAsync(context, seed, seed.AlfalfaId, 4m, animalId: animal1.Id);
        await RecordFeedAsync(context, seed, seed.AlfalfaId, 120m, locationId: seed.Barn1);

        var healthExpenseId = Guid.NewGuid();
        AddExpense(context, seed, 250m, new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc), id: healthExpenseId);
        AddExpense(context, seed, 25m, new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Utc), animalId: animal1.Id);
        AddExpense(context, seed, 50m, new DateTime(2025, 6, 11, 0, 0, 0, DateTimeKind.Utc), locationId: seed.Barn1);
        AddExpense(context, seed, 100m, new DateTime(2025, 6, 12, 0, 0, 0, DateTimeKind.Utc));
        AddIncome(context, seed, 300m, new DateTime(2025, 6, 13, 0, 0, 0, DateTimeKind.Utc), animalId: animal2.Id);
        AddIncome(context, seed, 100m, new DateTime(2025, 6, 13, 0, 0, 0, DateTimeKind.Utc));
        AddSalary(context, seed, 600m);

        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            AnimalId = animal1.Id,
            Symptoms = "Mastitis",
            Cost = 250m,
            DateRecorded = new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            ExpenseId = healthExpenseId
        });
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;
        var reconciliation = report.Reconciliation;

        // The feed and health figures are their own services' numbers, not re-derived here.
        var feedSummary = (await CreateFeedService(context).GetCostSummaryAsync(seed.FarmId, RangeFrom, RangeTo)).Value!;
        var healthSummary = (await new HealthCostService(context)
            .GetCostSummaryAsync(seed.FarmId, new FMS.Application.Health.HealthCostFilter { From = RangeFrom, To = RangeTo })).Value!;

        Assert.Equal(feedSummary.TotalConsumedCost, reconciliation.FeedConsumedTotal);
        Assert.Equal(healthSummary.GrandTotal, reconciliation.HealthRecordsTotal);
        Assert.Equal(620m, reconciliation.FeedConsumedTotal);
        Assert.Equal(250m, reconciliation.HealthRecordsTotal);

        Assert.Equal(425m, reconciliation.FarmExpensesTotal);
        Assert.Equal(250m, reconciliation.HealthLinkedExpenses);
        Assert.Equal(25m, reconciliation.ExpensesAttributedToAnimals);
        Assert.Equal(50m, reconciliation.ExpensesAllocatedFromLocations);
        Assert.Equal(100m, reconciliation.ExpensesAllocatedFromFarmPool);
        Assert.Equal(0m, reconciliation.ExpensesUnallocated);

        Assert.Equal(20m, reconciliation.FeedAttributedToAnimals);
        Assert.Equal(600m, reconciliation.FeedAllocatedFromLocations);
        Assert.Equal(0m, reconciliation.FeedUnallocated);

        Assert.Equal(600m, reconciliation.LabourTotal);
        Assert.Equal(1220m, reconciliation.CostOutsideTheExpenseLedger);

        Assert.True(reconciliation.ExpensesReconcile);
        Assert.True(reconciliation.FeedReconciles);
        Assert.True(reconciliation.HealthReconciles);
        Assert.DoesNotContain(report.Warnings, item => item.Code == CostWarningCodes.RowsUnassigned);

        // The farm's income is fully attributed: 300 named an animal, 100 shared by animal-days
        // (50 each — both animals are present for the whole range).
        Assert.Equal(400m, report.Farm.TotalRevenue);
        Assert.Equal(50m, Component(Row(report, "A-2"), CostComponentKeys.IncomeFarm).Amount);
        Assert.Equal(50m, Component(Row(report, "A-1"), CostComponentKeys.IncomeFarm).Amount);
        Assert.Equal(350m, Row(report, "A-2").TotalRevenue);
    }

    // ── caveats instead of confident-looking numbers ────────

    [Fact]
    public async Task GetReport_WithNoFeedRecordsSaysSoRatherThanShowingZeroFeed()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        await AddAnimalAsync(context, seed, "A-1");
        AddSalary(context, seed, 100m);
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        var warning = Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.FeedNotRecorded);
        Assert.Contains("missing", warning.Message);

        // No row claims a feed cost, and the report says why rather than implying feed was free.
        Assert.All(report.Animals, row => Assert.DoesNotContain(row.Costs, item => item.Key.StartsWith("feed.")));
        Assert.Equal(100m, report.Farm.TotalCost);
    }

    [Fact]
    public async Task GetReport_WithNoSalaryPaymentsSaysLabourIsMissingNotZero()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        await AddAnimalAsync(context, seed, "A-1");
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        var warning = Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.LabourNotRecorded);
        Assert.Contains("not the same as labour having cost nothing", warning.Message);
    }

    /// <summary>Cost this report knows exists but cannot price is named, with how many records, rather than estimated.</summary>
    [Fact]
    public async Task GetReport_NamesTheCostsItDeliberatelyDoesNotPrice()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var animal = await AddAnimalAsync(context, seed, "A-1");
        var inRange = new DateTime(2025, 6, 12, 0, 0, 0, DateTimeKind.Utc);

        context.MedicineUsages.Add(new MedicineUsage
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            MedicineId = Guid.NewGuid(),
            MedicineStockId = Guid.NewGuid(),
            QuantityUsed = 2,
            DateUsed = inRange
        });
        context.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            InventoryItemId = Guid.NewGuid(),
            MovementType = InventoryMovementType.Consumption,
            Quantity = 3,
            MovementDate = inRange
        });
        context.FeedStockMovements.Add(new FeedStockMovement
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            FeedTypeId = seed.AlfalfaId,
            MovementType = StockMovementType.Purchase,
            Quantity = 10,
            UnitCost = 5m,
            TotalCost = 50m,
            MovementDate = inRange
        });
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        Assert.Equal(1, Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.MedicineCostNotRecorded).AffectedCount);
        Assert.Equal(1, Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.InventoryConsumptionNotCosted).AffectedCount);

        var purchases = Assert.Single(report.Warnings, item => item.Code == CostWarningCodes.FeedPurchasesExcluded);
        Assert.Equal(1, purchases.AffectedCount);
        Assert.Contains("twice", purchases.Message);

        // The purchase is not added to cost: consumption is the cost basis.
        Assert.Equal(0m, Row(report, "A-1").TotalCost);
        Assert.Equal(animal.Id, Row(report, "A-1").AnimalId);
    }

    // ── rollups, isolation, validation ──────────────────────

    [Fact]
    public async Task GetReport_HerdAndFarmRollupsAreExactlyTheSumOfTheirRows()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        await StockUpAsync(context, seed);

        var barn1Animal = await AddAnimalAsync(context, seed, "A-B1");
        var barn2Animal = await AddAnimalAsync(context, seed, "A-B2", seed.Barn2);

        await RecordFeedAsync(context, seed, seed.AlfalfaId, 4m, animalId: barn1Animal.Id);
        await RecordFeedAsync(context, seed, seed.SilageId, 5m, animalId: barn2Animal.Id);
        AddSalary(context, seed, 60m);
        AddIncome(context, seed, 500m, new DateTime(2025, 6, 14, 0, 0, 0, DateTimeKind.Utc), animalId: barn2Animal.Id);
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        Assert.Equal(report.Animals.Sum(row => row.TotalCost), report.Farm.TotalCost);
        Assert.Equal(report.Animals.Sum(row => row.TotalRevenue), report.Farm.TotalRevenue);
        Assert.Equal(report.Animals.Sum(row => row.Margin), report.Farm.Margin);
        Assert.Equal(report.Animals.Sum(row => row.AnimalDays), report.TotalAnimalDays);

        Assert.Equal(report.Animals.Sum(row => row.TotalCost), report.Herds.Sum(herd => herd.TotalCost));
        Assert.Equal(report.Animals.Sum(row => row.TotalRevenue), report.Herds.Sum(herd => herd.TotalRevenue));
        Assert.Equal(2, report.Herds.Count);

        // Barn 2: 5 kg of silage at 2.00 direct, plus its share of the 60 salary over 60 animal-days.
        var barn2 = Assert.Single(report.Herds, herd => herd.LocationId == seed.Barn2);
        Assert.Equal("Barn 2", barn2.LocationName);
        Assert.Equal(1, barn2.AnimalCount);
        Assert.Equal(40m, barn2.TotalCost);
        Assert.Equal(500m, barn2.TotalRevenue);
        Assert.Equal(30, barn2.AnimalDays);
    }

    [Fact]
    public async Task GetReport_DoesNotIncludeAnotherFarmsRecords()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var mine = await AddAnimalAsync(context, seed, "A-MINE");

        // Another farm's animal, expense, income, salary and feed record.
        var otherAnimal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = seed.OtherFarmId,
            TagNumber = "OTHER-1",
            AnimalTypeId = seed.AnimalTypeId,
            SexOptionId = seed.SexOptionId,
            AnimalStatusId = seed.ActiveStatus,
            AcquisitionDate = Acquisition
        };
        context.Animals.Add(otherAnimal);
        context.Expenses.Add(new Expense
        {
            Id = Guid.NewGuid(),
            FarmId = seed.OtherFarmId,
            ExpenseDate = new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            Amount = 999m,
            ExpenseCategoryId = Guid.NewGuid(),
            PaymentMethodId = Guid.NewGuid()
        });
        context.IncomeRecords.Add(new IncomeRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.OtherFarmId,
            IncomeDate = new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            Amount = 888m,
            IncomeCategoryId = Guid.NewGuid(),
            PaymentMethodId = Guid.NewGuid()
        });
        context.SalaryPayments.Add(new SalaryPayment
        {
            Id = Guid.NewGuid(),
            FarmId = seed.OtherFarmId,
            EmployeeId = Guid.NewGuid(),
            Amount = 777m,
            PaymentDate = new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Utc),
            SalaryType = SalaryType.Monthly
        });
        await context.SaveChangesAsync();

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        Assert.Single(report.Animals, row => row.AnimalId == mine.Id);
        Assert.DoesNotContain(report.Animals, row => row.AnimalId == otherAnimal.Id);
        Assert.Equal(0m, report.Farm.TotalCost);
        Assert.Equal(0m, report.Reconciliation.FarmExpensesTotal);
        Assert.Equal(0m, report.Reconciliation.LabourTotal);
    }

    [Fact]
    public async Task GetReport_RejectsARangeThatEndsBeforeItStarts()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var result = await CreateService(context).GetCostPerAnimalReportAsync(seed.FarmId, new CostReportFilter
        {
            From = RangeTo,
            To = RangeFrom
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("before", result.Error!.Message);
    }

    /// <summary>The default window is the feed report's, so two reports can't have two defaults.</summary>
    [Fact]
    public async Task GetReport_WithNoDatesUsesTheFeedReportsThirtyDayWindow()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var result = await CreateService(context).GetCostPerAnimalReportAsync(seed.FarmId, new CostReportFilter());
        var report = result.Value!;

        Assert.Equal(DateTime.UtcNow.Date, report.To);
        Assert.Equal(DateTime.UtcNow.Date.AddDays(-CostAttributionService.DefaultRangeDays), report.From);
    }

    [Fact]
    public async Task GetReport_StatesTheRulesItApplied()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);

        var report = (await RunAsync(context, seed.FarmId)).Value!;

        Assert.Contains(report.Rules, rule => rule.Key == CostAllocationMethods.Direct);
        Assert.Contains(report.Rules, rule => rule.Key == CostAllocationMethods.LocationAnimalDays);
        Assert.Contains(report.Rules, rule => rule.Key == CostAllocationMethods.FarmAnimalDays);
        Assert.All(report.Rules, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Description)));
    }
}
