using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// A farm with years of history behind it, not the small fixture the other tests use.
///
/// <para>
/// The shape is deliberately weighted towards the tables that grow without anybody
/// deciding to add them: weight records and feed records accumulate one per animal per
/// day, financial records one per transaction, attendance one per employee per shift. A
/// farm with this much history is the case the background design exists for — building
/// this archive synchronously inside a request would be the timeout the feature avoids.
/// </para>
///
/// <para>
/// Every foreign key is set to a real row, so the same seeder works against the InMemory
/// provider and against PostgreSQL, where the constraint is enforced.
/// </para>
/// </summary>
public static class LargeFarmSeeder
{
    public const int AnimalCount = 2_000;
    public const int WeightRecordCount = 15_000;
    public const int FeedRecordCount = 10_000;
    public const int ExpenseCount = 5_000;
    public const int IncomeCount = 5_000;
    public const int VaccinationCount = 3_000;
    public const int MedicalRecordCount = 2_000;
    public const int AttendanceCount = 1_000;

    /// <summary>The rows this seeder creates, excluding lookups — what the manifest must account for.</summary>
    public static int BusinessRowCount =>
        AnimalCount + WeightRecordCount + FeedRecordCount + ExpenseCount + IncomeCount
        + VaccinationCount + MedicalRecordCount + AttendanceCount;

    /// <summary>Seeds the farm and returns its id, having written every row.</summary>
    public static async Task<Guid> SeedAsync(FmsDbContext db)
    {
        var accountId = Guid.NewGuid();
        var farmId = Guid.NewGuid();

        db.Accounts.Add(new Account { Id = accountId, Name = "Large farm account" });
        db.Farms.Add(new Farm { Id = farmId, AccountId = accountId, Name = "Years of History Farm" });

        await db.SaveChangesAsync();

        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Cattle" };
        var breed = new Breed
        {
            Id = Guid.NewGuid(),
            Name = "Holstein",
            AnimalType = animalType,
            AnimalTypeId = animalType.Id,
            AverageGestationDays = 283
        };
        var female = new SexOption { Id = Guid.NewGuid(), FarmId = farmId, Value = "Female" };
        var male = new SexOption { Id = Guid.NewGuid(), FarmId = farmId, Value = "Male" };
        var active = new AnimalStatus
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Active",
            Category = FMS.Domain.Enums.AnimalStatusCategory.Active,
            IsSystemDefined = true
        };
        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Barn" };
        var location = new Location
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Main Barn",
            LocationTypeId = locationType.Id
        };
        var ageCategory = new AgeCategory
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Adult",
            MinDays = 365,
            MaxDays = 9999
        };
        var department = new Department { Id = Guid.NewGuid(), FarmId = farmId, Name = "Dairy" };
        var employeeRole = new EmployeeRole { Id = Guid.NewGuid(), FarmId = farmId, Name = "Milker" };
        var expenseCategory = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farmId, Name = "Feed" };
        var incomeCategory = new IncomeCategory { Id = Guid.NewGuid(), FarmId = farmId, Name = "Milk Sales" };
        var paymentMethod = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farmId, Name = "Cash" };
        var feedType = new FeedType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Hay" };
        var vaccineType = new VaccineType
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "FMD Vaccine",
            DefaultDosage = "5ml"
        };

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FirstName = "Large",
            LastName = "Farm Worker",
            Email = "large-farm@example.com",
            Department = department,
            EmployeeRole = employeeRole,
            SalaryType = FMS.Domain.Enums.SalaryType.Monthly,
            SalaryRate = 45000m,
            HireDate = DateTime.UtcNow.AddYears(-5)
        };

        db.AddRange(
            animalType, breed, female, male, active, locationType, location, ageCategory,
            department, employeeRole, expenseCategory, incomeCategory, paymentMethod, feedType,
            vaccineType, employee);

        await db.SaveChangesAsync();

        var animalIds = new List<Guid>(AnimalCount);

        for (var batch = 0; batch < AnimalCount; batch += 5_000)
        {
            var animals = new List<Animal>();

            for (var index = batch; index < Math.Min(batch + 5_000, AnimalCount); index++)
            {
                var id = Guid.NewGuid();
                animalIds.Add(id);

                animals.Add(new Animal
                {
                    Id = id,
                    FarmId = farmId,
                    TagNumber = $"BIG-{index:D6}",
                    Name = $"Cow {index}",
                    AnimalType = animalType,
                    Breed = breed,
                    SexOption = index % 2 == 0 ? female : male,
                    AnimalStatus = active,
                    Location = location,
                    AgeCategory = ageCategory,
                    DateOfBirth = DateTime.UtcNow.AddYears(-4).AddDays(index % 1_000),
                    AcquisitionDate = DateTime.UtcNow.AddYears(-3)
                });
            }

            db.Animals.AddRange(animals);
            await db.SaveChangesAsync();
        }

        // Weight records: the table that grows fastest on a real farm.
        for (var batch = 0; batch < WeightRecordCount; batch += 5_000)
        {
            var rows = new List<WeightRecord>();

            for (var index = batch; index < Math.Min(batch + 5_000, WeightRecordCount); index++)
            {
                rows.Add(new WeightRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    AnimalId = animalIds[index % animalIds.Count],
                    WeightKg = 350m + (index % 400),
                    RecordedAt = DateTime.UtcNow.AddDays(-900).AddMinutes(index)
                });
            }

            db.WeightRecords.AddRange(rows);
            await db.SaveChangesAsync();
        }

        for (var batch = 0; batch < FeedRecordCount; batch += 5_000)
        {
            var rows = new List<FeedRecord>();

            for (var index = batch; index < Math.Min(batch + 5_000, FeedRecordCount); index++)
            {
                rows.Add(new FeedRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    FeedTypeId = feedType.Id,
                    AnimalId = animalIds[index % animalIds.Count],
                    Quantity = 12m + (index % 8),
                    UnitCost = 0.45m,
                    FedAt = DateTime.UtcNow.AddDays(-900).AddDays(index / 100)
                });
            }

            db.FeedRecords.AddRange(rows);
            await db.SaveChangesAsync();
        }

        for (var batch = 0; batch < ExpenseCount; batch += 5_000)
        {
            var rows = new List<Expense>();

            for (var index = batch; index < Math.Min(batch + 5_000, ExpenseCount); index++)
            {
                rows.Add(new Expense
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    ExpenseDate = DateTime.UtcNow.AddDays(-900).AddDays(index / 6),
                    Amount = 50m + (index % 400),
                    Category = expenseCategory,
                    PaymentMethod = paymentMethod,
                    Description = $"Historic expense {index}"
                });
            }

            db.Expenses.AddRange(rows);
            await db.SaveChangesAsync();
        }

        for (var batch = 0; batch < IncomeCount; batch += 5_000)
        {
            var rows = new List<IncomeRecord>();

            for (var index = batch; index < Math.Min(batch + 5_000, IncomeCount); index++)
            {
                rows.Add(new IncomeRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    IncomeDate = DateTime.UtcNow.AddDays(-900).AddDays(index / 5),
                    Amount = 200m + (index % 900),
                    Category = incomeCategory,
                    PaymentMethod = paymentMethod,
                    Description = $"Historic sale {index}"
                });
            }

            db.IncomeRecords.AddRange(rows);
            await db.SaveChangesAsync();
        }

        for (var batch = 0; batch < VaccinationCount; batch += 5_000)
        {
            var rows = new List<VaccinationRecord>();

            for (var index = batch; index < Math.Min(batch + 5_000, VaccinationCount); index++)
            {
                rows.Add(new VaccinationRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    AnimalId = animalIds[index % animalIds.Count],
                    VaccineTypeId = vaccineType.Id,
                    DateGiven = DateTime.UtcNow.AddDays(-365).AddDays(index / 8),
                    Cost = 15m
                });
            }

            db.VaccinationRecords.AddRange(rows);
            await db.SaveChangesAsync();
        }

        for (var batch = 0; batch < MedicalRecordCount; batch += 5_000)
        {
            var rows = new List<MedicalRecord>();

            for (var index = batch; index < Math.Min(batch + 5_000, MedicalRecordCount); index++)
            {
                rows.Add(new MedicalRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    AnimalId = animalIds[index % animalIds.Count],
                    Symptoms = "Routine check",
                    Cost = 25m,
                    DateRecorded = DateTime.UtcNow.AddDays(-365).AddDays(index / 4),
                    Status = FMS.Domain.Enums.MedicalRecordStatus.Resolved
                });
            }

            db.MedicalRecords.AddRange(rows);
            await db.SaveChangesAsync();
        }

        for (var batch = 0; batch < AttendanceCount; batch += 5_000)
        {
            var rows = new List<AttendanceRecord>();

            for (var index = batch; index < Math.Min(batch + 5_000, AttendanceCount); index++)
            {
                rows.Add(new AttendanceRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    EmployeeId = employee.Id,
                    Date = DateTime.UtcNow.AddDays(-300).AddDays(index),
                    Status = FMS.Domain.Enums.AttendanceStatus.Present,
                    CheckInAt = DateTime.UtcNow.AddDays(-300).AddDays(index).AddHours(7),
                    CheckOutAt = DateTime.UtcNow.AddDays(-300).AddDays(index).AddHours(17)
                });
            }

            db.AttendanceRecords.AddRange(rows);
            await db.SaveChangesAsync();
        }

        return farmId;
    }

    /// <summary>
    /// The rows this farm must report, counted from the seeder's own constants — the number
    /// the manifest is checked against, so an export that dropped a table cannot pass by
    /// looking internally consistent.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ExpectedRowsByFile() => new Dictionary<string, int>
    {
        ["animals.csv"] = AnimalCount,
        ["weight-records.csv"] = WeightRecordCount,
        ["feed-records.csv"] = FeedRecordCount,
        ["expenses.csv"] = ExpenseCount,
        ["income-records.csv"] = IncomeCount,
        ["vaccination-records.csv"] = VaccinationCount,
        ["medical-records.csv"] = MedicalRecordCount,
        ["attendance-records.csv"] = AttendanceCount
    };
}
