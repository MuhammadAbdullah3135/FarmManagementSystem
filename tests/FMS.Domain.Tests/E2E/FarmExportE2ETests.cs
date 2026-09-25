using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FMS.Application.Farm.Export;
using FMS.Domain.Entities;
using FMS.Domain.Tests.Export;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Farm.Export;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The full-farm export over HTTP, through the real controllers and the real
/// <c>FarmContextMiddleware</c>.
///
/// <para>
/// What this covers that the unit tests cannot: the route's membership gate, the role
/// attribute, that a farm's archive cannot be requested by somebody outside it — and the
/// property the whole feature exists for, that the archive goes back into the importers
/// unmodified, exercised end to end against a second farm.
/// </para>
/// </summary>
public class FarmExportE2ETests : IClassFixture<FarmExportE2ETests.Factory>
{
    /// <summary>The seven files that can be fed back to an importer, with their route prefixes.</summary>
    private static readonly (string Prefix, string FileName)[] ReimportableFiles =
    [
        ("animals", "animals.csv"),
        ("employees", "employees.csv"),
        ("inventory-items", "inventory-items.csv"),
        ("inventory/suppliers", "suppliers.csv"),
        ("inventory/customers", "customers.csv"),
        ("finance/expenses", "expenses.csv"),
        ("finance/income-records", "income-records.csv")
    ];

    private readonly Factory _factory;

    public FarmExportE2ETests(Factory factory) => _factory = factory;

    private Guid SeedFarmId
    {
        get
        {
            _ = _factory.Host;
            return _factory.SeedFarmId;
        }
    }

    private HttpClient Client(Guid farmId) => _factory.CreateAuthenticatedClient(farmId);

    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        public Guid SeedFarmId { get; private set; }

        /// <summary>A farm the test user can export from and import into.</summary>
        public Guid DestinationFarmId { get; private set; }

        /// <summary>A farm the test user has no membership in at all.</summary>
        public Guid ForeignFarmId { get; private set; }

        /// <summary>A farm whose member role is too low to export.</summary>
        public Guid LimitedFarmId { get; private set; }

        /// <summary>
        /// A farm nobody has exported from yet. The record for a farm is deliberately
        /// reused across requests (one row per farm), so a test that needs the state
        /// <em>before</em> any build must use a farm no other test has touched.
        /// </summary>
        public Guid UntouchedFarmId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            SeedFarmId = seed.FarmId;

            // The seeded farm has animals, finance and medicine, but no HR or inventory
            // records — so add one of each, which is what gives all seven exported files
            // something to carry.
            var department = new Department { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Dairy" };
            var role = new EmployeeRole { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Milker" };

            db.Departments.Add(department);
            db.EmployeeRoles.Add(role);

            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                FirstName = "Seed",
                LastName = "Worker",
                Email = "seed-worker@example.com",
                Department = department,
                EmployeeRole = role,
                SalaryType = FMS.Domain.Enums.SalaryType.Monthly,
                SalaryRate = 45000m,
                HireDate = DateTime.UtcNow.AddYears(-1)
            });

            db.InventoryItems.Add(new InventoryItem
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                Name = "Seed feed bin",
                Unit = "kg",
                Quantity = 50,
                ReorderLevel = 5,
                UnitCost = 10m
            });

            db.Suppliers.Add(new Supplier
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                Name = "Seed supplies",
                ContactInfo = "+254 700 000001"
            });

            db.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                Name = "Seed dairy co",
                ContactInfo = "+254 700 000002"
            });

            // Every lookup the seeded farm's records name, so a file exported from there
            // can be imported here. The values match by name and not by id — which is what
            // makes the round trip a test of the exported content rather than of shared
            // foreign keys. Statuses in particular: an importer refuses a value the
            // destination farm does not have, exactly as it would for a hand-typed file.
            var destination = new Farm
            {
                Id = Guid.NewGuid(),
                AccountId = seed.AccountId,
                Name = "Destination Farm"
            };

            db.Farms.Add(destination);
            db.UserFarms.Add(new UserFarm
            {
                UserId = JwtTokenHelper.TestUserId,
                FarmId = destination.Id,
                Role = "FarmManager"
            });

            var cattle = new AnimalType { Id = Guid.NewGuid(), FarmId = destination.Id, Name = "Cattle" };
            var breed = new Breed
            {
                Id = Guid.NewGuid(),
                Name = "Holstein",
                AnimalType = cattle,
                AverageGestationDays = 283
            };
            var female = new SexOption { Id = Guid.NewGuid(), FarmId = destination.Id, Value = "Female" };
            var male = new SexOption { Id = Guid.NewGuid(), FarmId = destination.Id, Value = "Male" };
            var active = new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Active",
                Category = FMS.Domain.Enums.AnimalStatusCategory.Active,
                IsSystemDefined = true
            };
            var sick = new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Sick",
                Category = FMS.Domain.Enums.AnimalStatusCategory.Inactive,
                IsSystemDefined = true
            };
            var sold = new AnimalStatus
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Sold",
                Category = FMS.Domain.Enums.AnimalStatusCategory.Terminal,
                IsSystemDefined = true
            };
            var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = destination.Id, Name = "Barn" };
            var location = new Location
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Main Barn",
                LocationTypeId = locationType.Id
            };
            var ageCategory = new AgeCategory
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Adult",
                MinDays = 365,
                MaxDays = 9999
            };
            var destinationDepartment = new Department
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Dairy"
            };
            var destinationRole = new EmployeeRole
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Milker"
            };
            var expenseCategory = new ExpenseCategory
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Feed"
            };
            var incomeCategory = new IncomeCategory
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Milk Sales"
            };
            var paymentMethod = new PaymentMethod
            {
                Id = Guid.NewGuid(),
                FarmId = destination.Id,
                Name = "Cash"
            };

            db.AddRange(
                cattle, breed, female, male, active, sick, sold, locationType, location, ageCategory,
                destinationDepartment, destinationRole, expenseCategory, incomeCategory, paymentMethod);

            // A farm the caller is a member of but with a role that may not export.
            var limited = new Farm { Id = Guid.NewGuid(), AccountId = seed.AccountId, Name = "Limited Farm" };
            db.Farms.Add(limited);
            db.UserFarms.Add(new UserFarm
            {
                UserId = JwtTokenHelper.TestUserId,
                FarmId = limited.Id,
                Role = "Employee"
            });

            // A farm the caller has no membership in whatsoever.
            var foreign = new Farm { Id = Guid.NewGuid(), AccountId = seed.AccountId, Name = "Foreign Farm" };
            db.Farms.Add(foreign);

            // A clean farm for the "before any build" assertions, with the same lookups so
            // an archive of it is valid even though no test ever builds it.
            var untouched = new Farm
            {
                Id = Guid.NewGuid(),
                AccountId = seed.AccountId,
                Name = "Untouched Farm"
            };
            db.Farms.Add(untouched);
            db.UserFarms.Add(new UserFarm
            {
                UserId = JwtTokenHelper.TestUserId,
                FarmId = untouched.Id,
                Role = "FarmManager"
            });
            db.AnimalTypes.Add(new AnimalType { Id = Guid.NewGuid(), FarmId = untouched.Id, Name = "Cattle" });

            DestinationFarmId = destination.Id;
            LimitedFarmId = limited.Id;
            ForeignFarmId = foreign.Id;
            UntouchedFarmId = untouched.Id;

            await db.SaveChangesAsync();
        }
    }

    // ── lifecycle ───────────────────────────────────────────────

    [Fact]
    public async Task RequestBuildsInBackground_ThenStatusThenDownload()
    {
        // The untouched farm: the assertions below are about the state before any build,
        // and the export record is reused across requests for a farm.
        var farmId = _factory.UntouchedFarmId;
        var client = Client(farmId);

        var jobs = (RecordingBackgroundJobClient)_factory.Services
            .GetRequiredService<IBackgroundJobClient>();

        // The fixture is shared by this class, so what is counted is what this test added:
        // earlier tests will already have queued exports for the same farm, and the record
        // for a farm is deliberately reused across requests.
        var jobsBefore = jobs.ExportJobs().Count;

        using var requested = await client.PostAsync($"/api/farm/{farmId}/export", null);

        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        var queued = await requested.Content.ReadFromJsonAsync<FarmExportDto>();
        Assert.NotNull(queued);
        Assert.Equal(FarmExportStatus.Queued, queued!.Status);
        Assert.False(queued.IsReady);

        // The job was handed to the queue with this farm and this export, and the status
        // endpoint answers while it has not run yet.
        Assert.Equal(jobsBefore + 1, jobs.ExportJobs().Count);
        Assert.Equal(farmId, jobs.ExportJobs()[^1].FarmId);
        Assert.Equal(queued.Id, jobs.ExportJobs()[^1].ExportId);

        using var notReady = await client.GetAsync($"/api/farm/{farmId}/export/download");
        Assert.Equal(HttpStatusCode.NotFound, notReady.StatusCode);

        // The worker's own entry point, exactly as Hangfire would call it.
        var export = jobs.ExportJobs()[^1];
        await RunExportJobAsync(export.ExportId, export.FarmId);

        using var status = await client.GetAsync($"/api/farm/{farmId}/export");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        var built = await status.Content.ReadFromJsonAsync<FarmExportDto>();
        Assert.NotNull(built);
        Assert.Equal(FarmExportStatus.Completed, built!.Status);
        Assert.True(built.IsReady);
        Assert.True(built.SizeBytes > 0);
        Assert.NotNull(built.Manifest);
        Assert.Equal(FarmExportEntitiesCount, built.Manifest!.Files.Count);

        using var download = await client.GetAsync($"/api/farm/{farmId}/export/download");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(download.Content.Headers.ContentDisposition?.FileName);

        var archive = await download.Content.ReadAsByteArrayAsync();
        var entries = FarmExportTestSupport.ReadEntries(archive);

        Assert.Contains("animals.csv", entries.Keys);
        Assert.Contains(FarmExportAssembler.ManifestFileName, entries.Keys);
        Assert.Equal(
            built.Manifest.Files.Select(file => file.FileName).ToHashSet(StringComparer.Ordinal),
            entries.Keys
                .Where(name => name != "manifest.json" && name != "README.txt")
                .ToHashSet(StringComparer.Ordinal));
    }

    private static int FarmExportEntitiesCount =>
        FMS.Infrastructure.Farm.Export.FarmExportEntities.Tables.Count;

    [Fact]
    public async Task RequestTwiceWhileBuilding_QueuesOneJobAndReturnsTheSameExport()
    {
        var farmId = SeedFarmId;
        var client = Client(farmId);

        var jobs = (RecordingBackgroundJobClient)_factory.Services
            .GetRequiredService<IBackgroundJobClient>();

        var before = jobs.ExportJobs().Count;

        using var first = await client.PostAsync($"/api/farm/{farmId}/export", null);
        using var second = await client.PostAsync($"/api/farm/{farmId}/export", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<FarmExportDto>();
        var secondBody = await second.Content.ReadFromJsonAsync<FarmExportDto>();

        Assert.Equal(firstBody!.Id, secondBody!.Id);
        Assert.Equal(before + 1, jobs.ExportJobs().Count);

        // Finish it, so the shared fixture is left with a completed export rather than a
        // queued one the next test would inherit.
        var export = jobs.ExportJobs()[^1];
        await RunExportJobAsync(export.ExportId, export.FarmId);
    }

    // ── authorization ───────────────────────────────────────────

    [Fact]
    public async Task Request_FromOutsideTheFarm_IsForbidden()
    {
        var foreignFarmId = _factory.ForeignFarmId;

        // The middleware rejects this before the controller is reached: the caller is not
        // a member of the farm the header names.
        var client = _factory.CreateAuthenticatedClient(foreignFarmId);

        using var response = await client.PostAsync($"/api/farm/{foreignFarmId}/export", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Access denied", await response.Content.ReadAsStringAsync());

        var jobs = (RecordingBackgroundJobClient)_factory.Services
            .GetRequiredService<IBackgroundJobClient>();

        Assert.DoesNotContain(jobs.ExportJobs(), job => job.FarmId == foreignFarmId);
    }

    [Fact]
    public async Task Request_ByARoleThatCannotExport_IsForbidden()
    {
        var limitedFarmId = _factory.LimitedFarmId;
        var client = Client(limitedFarmId);

        using var response = await client.PostAsync($"/api/farm/{limitedFarmId}/export", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var status = await client.GetAsync($"/api/farm/{limitedFarmId}/export");
        Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task WithoutTheFarmHeader_MembershipIsCheckedFromTheRoute()
    {
        var farmId = SeedFarmId;
        var client = _factory.CreateAuthenticatedClient();

        using var response = await client.PostAsync($"/api/farm/{farmId}/export", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // ── the round trip ──────────────────────────────────────────

    [Fact]
    public async Task EveryExportedFile_ReimportsIntoAnotherFarmUnchanged()
    {
        var farmId = SeedFarmId;
        var client = Client(farmId);

        using var requested = await client.PostAsync($"/api/farm/{farmId}/export", null);
        var queued = await requested.Content.ReadFromJsonAsync<FarmExportDto>();

        var jobs = (RecordingBackgroundJobClient)_factory.Services
            .GetRequiredService<IBackgroundJobClient>();

        var export = jobs.ExportJobs().Last(job => job.FarmId == farmId && job.ExportId == queued!.Id);
        await RunExportJobAsync(export.ExportId, export.FarmId);

        byte[] archive;
        ManifestDto manifest;

        using (var download = await client.GetAsync($"/api/farm/{farmId}/export/download"))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);

            archive = await download.Content.ReadAsByteArrayAsync();
        }

        manifest = ManifestFor(archive);
        var entries = FarmExportTestSupport.ReadEntries(archive);

        var destination = Client(_factory.DestinationFarmId);

        foreach (var (prefix, fileName) in ReimportableFiles)
        {
            var described = manifest.Files.Single(file => file.FileName == fileName);
            Assert.True(described.Reimportable, $"{fileName} should be re-importable");

            var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(entries[fileName]);
            file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            content.Add(file, "file", fileName);

            using var response = await destination.PostAsync(
                $"/api/farm/{_factory.DestinationFarmId}/{prefix}/import/commit", content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var commit = await response.Content.ReadFromJsonAsync<CommitShape>();

            Assert.NotNull(commit);

            var problems = string.Join(
                "; ",
                commit!.InvalidRows.SelectMany(row => row.Errors).Select(error => error.Message));

            Assert.True(
                described.RowCount == commit.ImportedCount,
                fileName + ": the archive carries " + described.RowCount + " rows but the import created "
                    + commit.ImportedCount + " (" + commit.TotalRows + " read, "
                    + commit.InvalidRows.Count + " rejected: " + problems + ")");

            Assert.Empty(commit.InvalidRows);
        }

        // The destination farm now holds what the archive described — the proof that the
        // export round-tripped rather than merely uploaded.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();

        Assert.Equal(manifest.Files.Single(file => file.FileName == "animals.csv").RowCount,
            await db.Animals.CountAsync(animal => animal.FarmId == _factory.DestinationFarmId));
        Assert.Equal(1,
            await db.Suppliers.CountAsync(supplier => supplier.FarmId == _factory.DestinationFarmId));
        Assert.Equal(1,
            await db.Customers.CountAsync(customer => customer.FarmId == _factory.DestinationFarmId));
    }

    // ── notification ────────────────────────────────────────────

    [Fact]
    public async Task ACompletedExport_NotifiesTheRequesterInTheApp()
    {
        var farmId = SeedFarmId;
        var client = Client(farmId);

        using var requested = await client.PostAsync($"/api/farm/{farmId}/export", null);
        var queued = await requested.Content.ReadFromJsonAsync<FarmExportDto>();

        var jobs = (RecordingBackgroundJobClient)_factory.Services
            .GetRequiredService<IBackgroundJobClient>();

        var export = jobs.ExportJobs().Last(job => job.FarmId == farmId && job.ExportId == queued!.Id);
        await RunExportJobAsync(export.ExportId, export.FarmId);

        using var notifications = await client.GetAsync($"/api/farm/{farmId}/notifications?includeResolved=true");

        Assert.Equal(HttpStatusCode.OK, notifications.StatusCode);

        var body = await notifications.Content.ReadFromJsonAsync<NotificationListShape>();

        var notices = body!.Items
            .Where(item => item.AlertType == FMS.Application.Notifications.NotificationAlertTypes.ExportReady)
            .ToList();

        // More than one may exist across this class's shared fixture; what matters is that
        // this build produced one, and that none of them has been resolved.
        Assert.NotEmpty(notices);

        var notice = notices[^1];

        Assert.False(notice.IsRead);
        Assert.False(notice.IsResolved);
        Assert.Equal(
            FMS.Application.Notifications.NotificationAlertTypes.ExportReadyLink,
            notice.Link);
    }

    // ── helpers ────────────────────────────────────────────────

    private async Task RunExportJobAsync(Guid exportId, Guid farmId)
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<FarmExportJob>();

        await job.ExecuteAsync(farmId, exportId);
    }

    private static ManifestDto ManifestFor(byte[] archive)
    {
        var entries = FarmExportTestSupport.ReadEntries(archive);
        var json = Encoding.UTF8.GetString(entries[FarmExportAssembler.ManifestFileName]);

        return System.Text.Json.JsonSerializer.Deserialize<ManifestDto>(json)!;
    }

    private sealed class ManifestDto
    {
        public Guid FarmId { get; set; }

        public int TotalRowCount { get; set; }

        public List<ManifestFileDto> Files { get; set; } = new();
    }

    private sealed class ManifestFileDto
    {
        public string FileName { get; set; } = string.Empty;

        public int RowCount { get; set; }

        public bool Reimportable { get; set; }
    }

    private sealed class CommitShape
    {
        public int TotalRows { get; set; }

        public int ImportedCount { get; set; }

        public List<RowShape> InvalidRows { get; set; } = new();
    }

    private sealed class RowShape
    {
        public int RowNumber { get; set; }

        public List<ErrorShape> Errors { get; set; } = new();
    }

    private sealed class ErrorShape
    {
        public string Field { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    private sealed class NotificationListShape
    {
        public List<NotificationShape> Items { get; set; } = new();
    }

    private sealed class NotificationShape
    {
        public string AlertType { get; set; } = string.Empty;

        public bool IsRead { get; set; }

        public bool IsResolved { get; set; }

        public string? Link { get; set; }
    }
}
