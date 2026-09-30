using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

/// <summary>
/// Cross-module integrity: a VaccineType with a LinkedMedicineId must deduct
/// medicine stock through the SAME FIFO logic as manual medicine usage, in the
/// SAME transaction as the vaccination record — insufficient stock means NO
/// vaccination record is persisted.
/// </summary>
public class VaccinationLinkedMedicineTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private static VaccineService CreateVaccineService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private static MedicineService CreateMedicineService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed record Seed(
        Guid FarmId,
        Guid AnimalId,
        Guid VaccineTypeId,
        Guid MedicineId,
        Guid FirstStockId,
        Guid SecondStockId);

    /// <summary>
    /// Seeds a farm with a linked vaccine type and up to two stock batches.
    /// Batch A always expires sooner, so FIFO must drain it first.
    /// </summary>
    private static async Task<Seed> SeedAsync(
        FmsDbContext context,
        int batchAQuantity = 100,
        int batchBQuantity = 0,
        int batchAExpiryDays = 30,
        int batchBExpiryDays = 90)
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var medicine = new Domain.Entities.Medicine
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "FMD Vaccine", Unit = "dose",
            LowStockThreshold = 10, ExpiringSoonDays = 30
        };
        var stockA = new Domain.Entities.MedicineStock
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, MedicineId = medicine.Id,
            BatchNumber = "B-A", Quantity = batchAQuantity, UnitCost = 2.0m,
            ExpiryDate = DateTime.UtcNow.AddDays(batchAExpiryDays), DateReceived = DateTime.UtcNow.AddDays(-30)
        };
        var stockB = new Domain.Entities.MedicineStock
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, MedicineId = medicine.Id,
            BatchNumber = "B-B", Quantity = batchBQuantity, UnitCost = 2.0m,
            ExpiryDate = DateTime.UtcNow.AddDays(batchBExpiryDays), DateReceived = DateTime.UtcNow.AddDays(-30)
        };
        var type = new Domain.Entities.AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var breed = new Domain.Entities.Breed { Id = Guid.NewGuid(), AnimalTypeId = type.Id, Name = "Holstein" };
        var sex = new Domain.Entities.SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new Domain.Entities.AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };
        var vaxType = new Domain.Entities.VaccineType
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "FMD", LinkedMedicineId = medicine.Id
        };
        var animal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "T1", Name = "Bella",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id
        };

        context.Farms.Add(farm);
        context.Medicines.Add(medicine);
        context.MedicineStocks.AddRange(stockA, stockB);
        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.VaccineTypes.Add(vaxType);
        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, animal.Id, vaxType.Id, medicine.Id, stockA.Id, stockB.Id);
    }

    private static async Task<VaccinationRecordDto> CreateVaccinationAsync(
        VaccineService vaccineService, Seed seed, int? quantityUsed = null, decimal cost = 0)
    {
        var vaccination = await vaccineService.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow,
            QuantityUsed = quantityUsed,
            Cost = cost // avoid auto expense unless a test opts in, isolating stock logic
        });
        Assert.True(vaccination.IsSuccess, vaccination.Error?.Message ?? "vaccination failed");
        return vaccination.Value!;
    }

    [Fact]
    public async Task Vaccination_WithLinkedMedicine_DeductsStockViaFifo()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var vaccineService = CreateVaccineService(context);

        // Sanity: manual medicine usage DOES deduct via FIFO
        var medicineService = CreateMedicineService(context);
        var usage = await medicineService.RecordUsageAsync(seed.FarmId, new RecordMedicineUsageRequest
        {
            MedicineId = seed.MedicineId,
            QuantityUsed = 5
        });
        Assert.True(usage.IsSuccess);
        var afterManual = await context.MedicineStocks.SingleAsync(s => s.Id == seed.FirstStockId);
        Assert.Equal(95, afterManual.Quantity);

        // A vaccination against the linked vaccine type must also deduct stock (default 1 dose)
        await CreateVaccinationAsync(vaccineService, seed);

        var after = await context.MedicineStocks.SingleAsync(s => s.Id == seed.FirstStockId);
        Assert.Equal(94, after.Quantity);

        var usageRow = await context.MedicineUsages.SingleAsync(u => u.Notes!.StartsWith("Vaccination "));
        Assert.Equal(1, usageRow.QuantityUsed);
    }

    [Fact]
    public async Task Vaccination_WithoutQuantity_DeductsOne()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var vaccineService = CreateVaccineService(context);

        // QuantityUsed omitted entirely — backwards-compatible default of 1 dose.
        await CreateVaccinationAsync(vaccineService, seed, quantityUsed: null);

        var after = await context.MedicineStocks.SingleAsync(s => s.Id == seed.FirstStockId);
        Assert.Equal(99, after.Quantity);
    }

    [Fact]
    public async Task Vaccination_DosageGreaterThanOne_DeductsCorrectQuantity()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var vaccineService = CreateVaccineService(context);

        await CreateVaccinationAsync(vaccineService, seed, quantityUsed: 4);

        var after = await context.MedicineStocks.SingleAsync(s => s.Id == seed.FirstStockId);
        Assert.Equal(96, after.Quantity);

        var usageRow = await context.MedicineUsages.SingleAsync(u => u.Notes!.StartsWith("Vaccination "));
        Assert.Equal(4, usageRow.QuantityUsed);
    }

    [Fact]
    public async Task Vaccination_MultiBatch_DeductsAcrossBatchesFifo()
    {
        using var context = CreateContext();
        // Batch A: 3 units, expires in 30 days (drained FIRST).
        // Batch B: 10 units, expires in 90 days.
        var seed = await SeedAsync(context, batchAQuantity: 3, batchBQuantity: 10);
        var vaccineService = CreateVaccineService(context);

        await CreateVaccinationAsync(vaccineService, seed, quantityUsed: 5);

        var batchA = await context.MedicineStocks.SingleAsync(s => s.Id == seed.FirstStockId);
        var batchB = await context.MedicineStocks.SingleAsync(s => s.Id == seed.SecondStockId);
        Assert.Equal(0, batchA.Quantity);  // sooner-expiry batch drained first
        Assert.Equal(8, batchB.Quantity);  // remainder taken from the later batch

        // Single usage row for the total, linked to the last batch deducted (same
        // convention as MedicineService.RecordUsageAsync).
        var usageRow = await context.MedicineUsages.SingleAsync(u => u.Notes!.StartsWith("Vaccination "));
        Assert.Equal(5, usageRow.QuantityUsed);
        Assert.Equal(seed.SecondStockId, usageRow.MedicineStockId);
    }

    [Fact]
    public async Task Vaccination_InsufficientStock_RollsBackAndDoesNotPersist()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context, batchAQuantity: 2);
        var vaccineService = CreateVaccineService(context);

        var vaccination = await vaccineService.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow,
            QuantityUsed = 5, // only 2 available
            Cost = 0
        });

        Assert.False(vaccination.IsSuccess);
        Assert.Contains("Insufficient", vaccination.Error?.Message);

        // The critical integrity assertion: NO vaccination record persisted,
        // stock untouched, no usage row created.
        Assert.Empty(await context.VaccinationRecords.ToListAsync());
        Assert.Empty(await context.MedicineUsages.ToListAsync());
        Assert.Empty(await context.AnimalTimelineEvents.ToListAsync());

        var batchA = await context.MedicineStocks.SingleAsync(s => s.Id == seed.FirstStockId);
        Assert.Equal(2, batchA.Quantity);
    }

    [Fact]
    public async Task Vaccination_InsufficientStockWithCost_AlsoLeavesNoExpense()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context, batchAQuantity: 1);
        var vaccineService = CreateVaccineService(context);

        var vaccination = await vaccineService.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow,
            QuantityUsed = 3,
            Cost = 50 // would auto-create an expense if the record were persisted
        });

        Assert.False(vaccination.IsSuccess);
        Assert.Empty(await context.VaccinationRecords.ToListAsync());
        Assert.Empty(await context.Expenses.ToListAsync());
    }
}
