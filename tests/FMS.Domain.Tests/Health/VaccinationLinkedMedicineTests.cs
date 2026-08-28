using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

/// <summary>
/// Cross-module check: a VaccineType with a LinkedMedicineId should deduct
/// medicine stock through the SAME FIFO logic as manual medicine usage.
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
        Guid StockId);

    private static async Task<Seed> SeedAsync(FmsDbContext context)
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var medicine = new Domain.Entities.Medicine
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "FMD Vaccine", Unit = "dose",
            LowStockThreshold = 10, ExpiringSoonDays = 30
        };
        var stock = new Domain.Entities.MedicineStock
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, MedicineId = medicine.Id,
            BatchNumber = "B1", Quantity = 100, UnitCost = 2.0m,
            ExpiryDate = DateTime.UtcNow.AddDays(60), DateReceived = DateTime.UtcNow.AddDays(-30)
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
        context.MedicineStocks.Add(stock);
        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.VaccineTypes.Add(vaxType);
        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, animal.Id, vaxType.Id, medicine.Id, stock.Id);
    }

    [Fact]
    public async Task Vaccination_WithLinkedMedicine_DoesNotDeductStock()
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
        var afterManual = await context.MedicineStocks.SingleAsync(s => s.Id == seed.StockId);
        Assert.Equal(95, afterManual.Quantity);

        // Now record a vaccination against the vaccine type that is LinkedMedicineId -> stock should drop
        var vaccination = await vaccineService.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow,
            Cost = 0 // avoid auto expense, isolate stock logic
        });
        Assert.True(vaccination.IsSuccess);

        var after = await context.MedicineStocks.SingleAsync(s => s.Id == seed.StockId);

        // EXPECTED (per cross-module requirement): stock drops because LinkedMedicineId ties it to FIFO.
        // ACTUAL (verified bug): stock unchanged because CreateVaccinationRecordAsync never reads LinkedMedicineId.
        Assert.True(
            after.Quantity < 95,
            $"Vaccination with LinkedMedicineId should deduct stock via FIFO. Expected < 95, actual {after.Quantity}.");
    }
}
