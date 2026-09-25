using System.IO.Compression;
using System.Text;
using FMS.Application.Farm.Export;
using FMS.Application.Jobs;
using FMS.Domain.Entities;
using FMS.Infrastructure.Farm.Export;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// The queue side of the export, as a test sees it: records what was handed over instead of
/// running it, so a test can assert both that a build was queued exactly once and that it
/// was not queued twice.
/// </summary>
public sealed class RecordingFarmExportQueue : IFarmExportQueue
{
    public List<(Guid FarmId, Guid ExportId)> Enqueued { get; } = new();

    public void Enqueue(Guid farmId, Guid exportId) => Enqueued.Add((farmId, exportId));
}

/// <summary>
/// One export farm as the tests build it: lookups every farm shares by name (so a file
/// exported from one can be imported into another) plus one row of each importable entity,
/// each carrying a marker unique to that farm.
///
/// <para>
/// The markers are the isolation test. Every row in farm A contains its own marker and
/// never farm B's, so "the archive contains only this farm" becomes a scan over every cell
/// of every file rather than a spot check of one table.
/// </para>
/// </summary>
public sealed record ExportSeed(
    Guid FarmId,
    string FarmName,
    string Marker,
    IReadOnlyList<string> AnimalTags,
    string EmployeeEmail,
    string InventoryItemName,
    string SupplierName,
    string CustomerName,
    string ExpenseDescription,
    string IncomeDescription);

/// <summary>Fixtures shared by the export tests.</summary>
public static class FarmExportTestSupport
{
    /// <summary>Two markers that can never appear in each other's archive.</summary>
    public const string AlphaMarker = "ALPHAUNIQUE";

    public const string BetaMarker = "BETAUNIQUE";

    public static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    public static Stream Bytes(byte[] bytes) => new MemoryStream(bytes);

    /// <summary>
    /// Seeds a complete farm: the lookups every importer resolves against, plus one of each
    /// importable entity, all named with <paramref name="marker"/>.
    /// </summary>
    public static async Task<ExportSeed> SeedFarmAsync(
        FmsDbContext db,
        string farmName,
        string marker,
        int animalCount = 3)
    {
        var accountId = Guid.NewGuid();
        var farmId = Guid.NewGuid();

        db.Accounts.Add(new Account { Id = accountId, Name = $"{farmName} account" });

        var farm = new Farm { Id = farmId, AccountId = accountId, Name = farmName };
        db.Farms.Add(farm);

        await db.SaveChangesAsync();

        await SeedLookupsAsync(db, farmId, marker);

        // ── animals ───────────────────────────────────────────────────────
        var animalType = await db.AnimalTypes.SingleAsync(t => t.FarmId == farmId && t.Name == "Cattle");

        // Two breeds exist per farm (see SeedLookupsAsync): this is the shared one, and
        // the marker-named one is what the isolation test looks for.
        var breed = await db.Breeds.SingleAsync(b => b.Name == "Holstein" && b.AnimalType.Id == animalType.Id);
        var female = await db.SexOptions.SingleAsync(s => s.FarmId == farmId && s.Value == "Female");
        var active = await db.AnimalStatuses.SingleAsync(s => s.FarmId == farmId && s.Name == "Active");

        var tags = new List<string>();
        var animals = new List<Animal>();

        for (var index = 1; index <= animalCount; index++)
        {
            var tag = $"{marker}-COW-{index:D3}";
            tags.Add(tag);

            animals.Add(new Animal
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                TagNumber = tag,
                Name = $"{marker} cow {index}",
                AnimalType = animalType,
                Breed = breed,
                SexOption = female,
                AnimalStatus = active,
                DateOfBirth = DateTime.UtcNow.AddYears(-3),
                AcquisitionDate = DateTime.UtcNow.AddYears(-3),
                Notes = $"{marker} notes"
            });
        }

        db.Animals.AddRange(animals);

        // ── employees, inventory, suppliers, customers, finance ──────────
        var department = await db.Departments.SingleAsync(d => d.FarmId == farmId);
        var employeeRole = await db.EmployeeRoles.SingleAsync(r => r.FarmId == farmId);
        var employeeEmail = $"{marker.ToLowerInvariant()}-worker@example.test";

        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            FirstName = "Test",
            LastName = $"{marker} Worker",
            Email = employeeEmail,
            Department = department,
            EmployeeRole = employeeRole,
            SalaryType = FMS.Domain.Enums.SalaryType.Monthly,
            SalaryRate = 45000m,
            HireDate = DateTime.UtcNow.AddYears(-1)
        });

        var inventoryItemName = $"{marker} feed bin";

        db.InventoryItems.Add(new InventoryItem
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = inventoryItemName,
            Unit = "kg",
            Quantity = 100,
            ReorderLevel = 10,
            UnitCost = 12.5m,
            Category = "Feed",
            Location = "Store room"
        });

        var supplierName = $"{marker} supplies";
        db.Suppliers.Add(new Supplier
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = supplierName,
            ContactInfo = "+254 700 000000",
            ProductsSupplied = "Feed, vet supplies"
        });

        var customerName = $"{marker} dairy co";
        db.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = customerName,
            ContactInfo = "+254 700 111111"
        });

        var expenseCategory = await db.ExpenseCategories.SingleAsync(c => c.FarmId == farmId);
        var incomeCategory = await db.IncomeCategories.SingleAsync(c => c.FarmId == farmId);
        var paymentMethod = await db.PaymentMethods.SingleAsync(p => p.FarmId == farmId);

        var expenseDescription = $"{marker} expense";

        db.Expenses.Add(new Expense
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            ExpenseDate = DateTime.UtcNow.AddDays(-10),
            Amount = 250.5m,
            Category = expenseCategory,
            PaymentMethod = paymentMethod,
            Description = expenseDescription
        });

        var incomeDescription = $"{marker} income";

        db.IncomeRecords.Add(new IncomeRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            IncomeDate = DateTime.UtcNow.AddDays(-5),
            Amount = 1500m,
            Category = incomeCategory,
            PaymentMethod = paymentMethod,
            Description = incomeDescription
        });

        await db.SaveChangesAsync();

        return new ExportSeed(
            farmId, farmName, marker, tags, employeeEmail, inventoryItemName,
            supplierName, customerName, expenseDescription, incomeDescription);
    }

    /// <summary>
    /// The lookups a farm needs before any import can resolve a value — the same names for
    /// every farm, because a file exported from one farm is imported into a farm that has
    /// to recognise its values. Name-identical and id-distinct is what makes the round trip
    /// a real test of the exported content rather than of shared ids.
    /// </summary>
    public static async Task SeedLookupsAsync(FmsDbContext db, Guid farmId, string marker)
    {
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Cattle" };
        var breed = new Breed
        {
            Id = Guid.NewGuid(),
            Name = "Holstein",
            AnimalTypeId = animalType.Id,
            AnimalType = animalType,
            AverageGestationDays = 283
        };

        // A second breed per farm, named for the farm: the breed table has no FarmId of its
        // own, so it is the one that proves child tables are scoped through their parent
        // rather than exported from every farm at once.
        var markerBreed = new Breed
        {
            Id = Guid.NewGuid(),
            Name = $"{marker} breed",
            AnimalType = animalType,
            AnimalTypeId = animalType.Id,
            AverageGestationDays = 280
        };

        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Barn" };
        var location = new Location
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = "Main Barn",
            LocationTypeId = locationType.Id
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

        // Feed types and diet plans: a diet-plan item has no FarmId either, so it needs the
        // same parent-scoped treatment as a breed.
        var feedType = new FeedType { Id = Guid.NewGuid(), FarmId = farmId, Name = $"{marker} feed" };
        var dietPlan = new DietPlan { Id = Guid.NewGuid(), FarmId = farmId, Name = $"{marker} plan" };
        var dietPlanItem = new DietPlanItem
        {
            Id = Guid.NewGuid(),
            DietPlanId = dietPlan.Id,
            DietPlan = dietPlan,
            FeedTypeId = feedType.Id,
            FeedType = feedType,
            QuantityPerFeeding = 2.5m
        };

        db.AddRange(
            animalType, breed, markerBreed, locationType, location, female, male, active, ageCategory,
            department, employeeRole, expenseCategory, incomeCategory, paymentMethod,
            feedType, dietPlan, dietPlanItem);

        await db.SaveChangesAsync();
    }

    /// <summary>Every entry of an archive, keyed by entry name.</summary>
    public static IReadOnlyDictionary<string, byte[]> ReadEntries(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);

        return zip.Entries.ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                return buffer.ToArray();
            },
            StringComparer.Ordinal);
    }

    /// <summary>The manifest an archive carries, read back from inside it.</summary>
    public static FMS.Application.Farm.Export.FarmExportManifestDto ReadManifest(byte[] archive)
    {
        var entries = ReadEntries(archive);
        var json = System.Text.Encoding.UTF8.GetString(entries[FarmExportAssembler.ManifestFileName]);

        return System.Text.Json.JsonSerializer.Deserialize<FarmExportManifestDto>(json)!;
    }

    /// <summary>
    /// An export harness: one InMemory database, one assembler, the service and the job
    /// wired together the way the container wires them, and file storage in a temp folder
    /// each test owns.
    /// </summary>
    public sealed class Harness : IDisposable
    {
        private readonly string _databaseName = $"export-{Guid.NewGuid():N}";

        public FmsDbContext Context { get; }

        public RecordingFarmExportQueue Queue { get; } = new();

        public FarmExportAssembler Assembler { get; } = new();

        public FileStorageService Files { get; }

        public FarmExportService Service { get; }

        public FarmExportJob Job { get; }

        public string StorageRoot { get; }

        /// <summary>
        /// The options the job reads. Exposed rather than fixed so a test can tighten a
        /// limit after a build has already succeeded — which is how the "a failed rebuild
        /// keeps the previous archive" case is produced through the real validation path.
        /// </summary>
        public FarmExportOptions ExportOptions { get; } = new();

        public Harness(JobOptions? jobs = null, FarmExportOptions? exportOptions = null)
        {
            Context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .Options);

            StorageRoot = Path.Combine(Path.GetTempPath(), $"fms-export-tests-{Guid.NewGuid():N}");
            Files = new FileStorageService(StorageRoot);

            ExportOptions = exportOptions ?? new FarmExportOptions();

            Service = new FarmExportService(
                Context,
                Queue,
                Files,
                Options.Create(jobs ?? new JobOptions()));

            Job = new FarmExportJob(
                Context,
                Assembler,
                Files,
                Options.Create(exportOptions ?? ExportOptions),
                NullLogger<FarmExportJob>.Instance);
        }

        /// <summary>
        /// Requests an export through the service and runs the job that request enqueued —
        /// the same entry point Hangfire would call, with no scheduler in between.
        /// </summary>
        public async Task<FarmExportDto> BuildAsync(Guid farmId, Guid userId)
        {
            var requested = await Service.RequestAsync(farmId, userId);
            Assert.True(requested.IsSuccess, requested.Error?.Message);

            var dto = requested.Value!;
            var queued = Queue.Enqueued.Last();

            await Job.ExecuteAsync(queued.FarmId, queued.ExportId);

            var status = await Service.GetLatestAsync(farmId);
            Assert.True(status.IsSuccess, status.Error?.Message);

            return status.Value!;
        }

        public void Dispose()
        {
            Context.Dispose();

            try
            {
                if (Directory.Exists(StorageRoot))
                {
                    Directory.Delete(StorageRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort: a leftover temp folder must never fail a test that passed.
            }
        }
    }
}
