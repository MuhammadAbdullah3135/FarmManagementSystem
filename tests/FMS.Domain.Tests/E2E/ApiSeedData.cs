using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Seeds a complete test farm with animals, health records, feed, finance, tasks, etc.
/// </summary>
public static class ApiSeedData
{
    public record SeedIds(
        Guid FarmId,
        Guid AccountId,
        Guid AnimalTypeId,
        Guid BreedId,
        Guid SexMaleId,
        Guid SexFemaleId,
        Guid ActiveStatusId,
        Guid SickStatusId,
        Guid SoldStatusId,
        Guid LocationId,
        Guid ExpenseCategoryId,
        Guid IncomeCategoryId,
        Guid PaymentMethodId,
        Guid MedicineId,
        Guid VaccineTypeId
    );

    public static async Task<SeedIds> SeedAsync(FmsDbContext db)
    {
        var accountId = Guid.NewGuid();
        var farmId = Guid.NewGuid();

        var account = new Account { Id = accountId, Name = "Test Account" };
        var farm = new Farm { Id = farmId, AccountId = accountId, Name = "E2E Test Farm" };
        db.Accounts.Add(account);
        db.Farms.Add(farm);

        // Seed a user that matches the test JWT token's NameIdentifier claim
        var testUser = new User
        {
            Id = JwtTokenHelper.TestUserId,
            AccountId = accountId,
            Email = "testuser@test.com",
            PasswordHash = "hashed",
            FirstName = "Test",
            LastName = "User"
        };
        db.Users.Add(testUser);

        // Link user to farm (required by FarmContextMiddleware)
        var userFarm = new UserFarm
        {
            UserId = JwtTokenHelper.TestUserId,
            FarmId = farmId,
            Role = "FarmManager"
        };
        db.UserFarms.Add(userFarm);

        // Animal types & breeds
        var cattleType = new AnimalType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Cattle" };
        var breed = new Breed { Id = Guid.NewGuid(), Name = "Holstein", AnimalType = cattleType, AverageGestationDays = 283 };
        db.AnimalTypes.Add(cattleType);
        db.Breeds.Add(breed);

        // Sex options
        var maleSex = new SexOption { Id = Guid.NewGuid(), Value = "Male" };
        var femaleSex = new SexOption { Id = Guid.NewGuid(), Value = "Female" };
        db.SexOptions.AddRange(maleSex, femaleSex);

        // Statuses
        var activeStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farmId, Name = "Active", Category = FMS.Domain.Enums.AnimalStatusCategory.Active, IsSystemDefined = true };
        var sickStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farmId, Name = "Sick", Category = FMS.Domain.Enums.AnimalStatusCategory.Inactive, IsSystemDefined = true };
        var soldStatus = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farmId, Name = "Sold", Category = FMS.Domain.Enums.AnimalStatusCategory.Terminal, IsSystemDefined = true };
        db.AnimalStatuses.AddRange(activeStatus, sickStatus, soldStatus);

        // Location
        var location = new Location { Id = Guid.NewGuid(), FarmId = farmId, Name = "Main Barn" };
        db.Locations.Add(location);

        // Finance
        var expCat = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farmId, Name = "Feed" };
        var incCat = new IncomeCategory { Id = Guid.NewGuid(), FarmId = farmId, Name = "Milk Sales" };
        var payMethod = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farmId, Name = "Cash" };
        db.ExpenseCategories.Add(expCat);
        db.IncomeCategories.Add(incCat);
        db.PaymentMethods.Add(payMethod);

        // Animals — 5 active, 2 sick, 1 sold
        var animals = new List<Animal>();
        for (int i = 1; i <= 5; i++)
        {
            animals.Add(new Animal
            {
                Id = Guid.NewGuid(), FarmId = farmId,
                TagNumber = $"E2E-{i:D3}", Name = $"Cow {i}",
                AnimalType = cattleType, Breed = breed,
                SexOption = femaleSex, AnimalStatus = activeStatus,
                Location = location, DateOfBirth = DateTime.UtcNow.AddYears(-2)
            });
        }
        // Sick animals
        animals.Add(new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "E2E-S1", Name = "Sick Cow 1",
            AnimalType = cattleType, Breed = breed,
            SexOption = femaleSex, AnimalStatus = sickStatus,
            DateOfBirth = DateTime.UtcNow.AddYears(-3)
        });
        animals.Add(new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "E2E-S2", Name = "Sick Cow 2",
            AnimalType = cattleType, Breed = breed,
            SexOption = maleSex, AnimalStatus = sickStatus,
            DateOfBirth = DateTime.UtcNow.AddYears(-1)
        });
        // Sold animal
        animals.Add(new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "E2E-X1", Name = "Sold Cow",
            AnimalType = cattleType, Breed = breed,
            SexOption = femaleSex, AnimalStatus = soldStatus,
            DateOfBirth = DateTime.UtcNow.AddYears(-4)
        });
        db.Animals.AddRange(animals);
        await db.SaveChangesAsync();

        var activeAnimalIds = animals.Where(a => a.AnimalStatusId == activeStatus.Id).Select(a => a.Id).ToList();
        var sickAnimalIds = animals.Where(a => a.AnimalStatusId == sickStatus.Id).Select(a => a.Id).ToList();

        // Medicine
        var medicine = new Medicine
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Name = "Oxytetracycline", Unit = "ml",
            LowStockThreshold = 50, ExpiringSoonDays = 30
        };
        db.Medicines.Add(medicine);

        var medicineStock = new MedicineStock
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            MedicineId = medicine.Id, BatchNumber = "BATCH-001",
            Quantity = 100, UnitCost = 2.5m,
            ExpiryDate = DateTime.UtcNow.AddDays(60),
            DateReceived = DateTime.UtcNow.AddDays(-30)
        };
        db.MedicineStocks.Add(medicineStock);

        // Vaccine type
        var vaccineType = new VaccineType
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Name = "FMD Vaccine", DefaultDosage = "5ml"
        };
        db.VaccineTypes.Add(vaccineType);

        // Expense
        var expense = new Expense
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Amount = 250, ExpenseCategoryId = expCat.Id,
            PaymentMethodId = payMethod.Id,
            ExpenseDate = DateTime.UtcNow.AddDays(-10),
            Description = "Feed purchase"
        };
        db.Expenses.Add(expense);

        // Income
        var income = new IncomeRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Amount = 1500, IncomeCategoryId = incCat.Id,
            PaymentMethodId = payMethod.Id,
            IncomeDate = DateTime.UtcNow.AddDays(-5),
            Description = "Milk sale"
        };
        db.IncomeRecords.Add(income);

        // Farm task
        var task = new FarmTask
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Title = "Clean barn", Priority = FMS.Domain.Enums.FarmTaskPriority.High,
            Status = FMS.Domain.Enums.FarmTaskStatus.Pending,
            DueDate = DateTime.UtcNow.AddDays(-3) // overdue
        };
        db.FarmTasks.Add(task);

        // Medical record
        var medicalRecord = new MedicalRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            AnimalId = sickAnimalIds[0],
            Symptoms = "Limping", Diagnosis = "Foot infection",
            Treatment = "Antibiotics", Cost = 75,
            DateRecorded = DateTime.UtcNow.AddDays(-7),
            Status = FMS.Domain.Enums.MedicalRecordStatus.Open
        };
        db.MedicalRecords.Add(medicalRecord);

        // Vaccination record
        var vaccRecord = new VaccinationRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            AnimalId = activeAnimalIds[0],
            VaccineTypeId = vaccineType.Id,
            DateGiven = DateTime.UtcNow.AddDays(-15),
            Cost = 25
        };
        db.VaccinationRecords.Add(vaccRecord);

        // Weight record
        var weightRecord = new WeightRecord
        {
            Id = Guid.NewGuid(),
            AnimalId = activeAnimalIds[0],
            WeightKg = 450,
            RecordedAt = DateTime.UtcNow.AddDays(-30)
        };
        db.WeightRecords.Add(weightRecord);

        // Gestation record (upcoming birth)
        var breedingRecord = new BreedingRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            SireId = animals[4].Id, DamId = activeAnimalIds[0],
            BreedingDate = DateTime.UtcNow.AddDays(-200),
            Method = FMS.Domain.Enums.BreedingMethod.Natural,
            Result = FMS.Domain.Enums.BreedingResult.Confirmed
        };
        db.BreedingRecords.Add(breedingRecord);

        var gestationRecord = new GestationRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            BreedingRecordId = breedingRecord.Id,
            AnimalId = activeAnimalIds[0],
            ConfirmedDate = DateTime.UtcNow.AddDays(-180),
            ExpectedDeliveryDate = DateTime.UtcNow.AddDays(10),
            CurrentStage = FMS.Domain.Enums.GestationStage.Late
        };
        db.GestationRecords.Add(gestationRecord);

        // Inventory
        var inventoryItem = new InventoryItem
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Name = "Hay Bales", Unit = "bales",
            Quantity = 5, ReorderLevel = 10, // low stock!
            UnitCost = 15
        };
        db.InventoryItems.Add(inventoryItem);

        await db.SaveChangesAsync();

        return new SeedIds(
            farmId, accountId,
            cattleType.Id, breed.Id,
            maleSex.Id, femaleSex.Id,
            activeStatus.Id, sickStatus.Id, soldStatus.Id,
            location.Id,
            expCat.Id, incCat.Id, payMethod.Id,
            medicine.Id, vaccineType.Id
        );
    }
}
