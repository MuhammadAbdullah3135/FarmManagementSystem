using System.Security.Claims;
using System.Text.Json;
using FMS.Application.Animal;
using FMS.Application.Attendance;
using FMS.Application.Common;
using FMS.Application.Sync;
using FMS.Application.Tasks;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Attendance;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Sync;
using FMS.Infrastructure.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FMS.Domain.Tests.Sync;

/// <summary>
/// The sync service's own guarantees, at the level the E2E tests cannot reach: the ledger
/// lookup, the outcome classification, and — the one that matters most for a device — that a
/// single item blowing up cannot take the rest of the queue with it.
/// </summary>
public class MutationSyncServiceTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task UnknownOperation_IsRejected_WithoutBeingApplied()
    {
        using var harness = SyncHarness.Create();

        var result = await harness.ApplyAsync(harness.RawItem("animal.create", Guid.NewGuid(), new { }));

        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Contains("Unknown operation", result.Items[0].Message);
        Assert.Equal(0, harness.LedgerCount());
    }

    [Fact]
    public async Task MissingMutationId_IsRejected()
    {
        using var harness = SyncHarness.Create();

        var result = await harness.ApplyAsync(harness.RawItem(SyncOperations.WeightRecord, Guid.Empty, new { }));

        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Equal("A client mutation id is required", result.Items[0].Message);
        Assert.Equal(0, harness.LedgerCount());
    }

    [Fact]
    public async Task UnreadablePayload_IsRejected_WithTheOperationNamed()
    {
        using var harness = SyncHarness.Create();

        var result = await harness.ApplyAsync(
            harness.RawItem(SyncOperations.TaskComplete, Guid.NewGuid(), "not an object"));

        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Contains("could not be read as a task completion", result.Items[0].Message);
    }

    [Fact]
    public async Task AcceptedMutation_IsRecordedOnce_AndItsReplayIsTheStoredResult()
    {
        using var harness = SyncHarness.Create();
        var mutationId = Guid.NewGuid();

        var first = await harness.ApplyAsync(harness.WeightItem(mutationId, 401.5m));
        var replay = await harness.ApplyAsync(harness.WeightItem(mutationId, 999m));

        Assert.Equal(SyncMutationOutcome.Accepted, first.Items[0].Outcome);
        Assert.Equal(1, harness.LedgerCount());

        // Identical in every field, including the payload — the replay never re-runs the workflow.
        Assert.Equal(
            JsonSerializer.Serialize(first.Items[0], WebOptions),
            JsonSerializer.Serialize(replay.Items[0], WebOptions));

        Assert.Equal(1, harness.WeightCount());
        Assert.Equal(401.5m, harness.Weight());
    }

    [Fact]
    public async Task SupersededMutation_LeavesNoLedgerRow()
    {
        using var harness = SyncHarness.Create();

        var first = await harness.ApplyAsync(harness.CheckInItem(Guid.NewGuid()));
        var second = await harness.ApplyAsync(harness.CheckInItem(Guid.NewGuid()));

        Assert.Equal(SyncMutationOutcome.Accepted, first.Items[0].Outcome);
        Assert.Equal(SyncMutationOutcome.Superseded, second.Items[0].Outcome);

        // Nothing was written by the superseded item, so it is not "processed": the day's record
        // is what satisfies the intent, and only the accepted mutation is on the ledger.
        Assert.Equal(1, harness.LedgerCount());
        Assert.Equal(1, harness.AttendanceCount());
        Assert.Equal(1, second.SuccessCount);
    }

    [Fact]
    public async Task OneItemFailingUnexpectedly_DoesNotStopTheItemsAfterIt()
    {
        // A workflow that throws is the case the outer guard exists for: the failed item is
        // reported without leaking the exception, and the queue keeps moving.
        using var harness = SyncHarness.Create(attendance: new ThrowingAttendanceService());

        var brokenId = Guid.NewGuid();
        var goodId = Guid.NewGuid();

        var result = await harness.ApplyAsync(
            harness.CheckInItem(brokenId),
            harness.WeightItem(goodId, 320m));

        Assert.Equal(2, result.RequestedCount);
        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Equal("The item could not be applied on the server", result.Items[0].Message);
        Assert.DoesNotContain("boom", result.Items[0].Message);

        Assert.Equal(SyncMutationOutcome.Accepted, result.Items[1].Outcome);
        Assert.Equal(1, harness.WeightCount());
        Assert.Equal(0, harness.LedgerCountFor(brokenId));
    }

    [Fact]
    public void EveryOperation_RequiresNoRoleBeyondFarmMembershipToday()
    {
        // The registry is the sync path's authorization contract. Every workflow currently requires
        // any farm member, so every entry is null; SyncAuthorizationParityTests is what binds this
        // to the three controllers' own attributes and fails if one of them gains a role.
        foreach (var operation in SyncOperations.All)
        {
            Assert.True(SyncOperations.IsKnown(operation));
            Assert.Null(SyncOperations.RequiredRoles(operation));
        }
    }
}

/// <summary>
/// A provider shaped like the API's request scope: an InMemory database, the real workflow
/// services, and the sync service resolving each item from its own child scope.
/// </summary>
internal sealed class SyncHarness : IDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private readonly ServiceProvider _provider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private SyncHarness(
        ServiceProvider provider, IHttpContextAccessor httpContextAccessor,
        Guid farmId, Guid animalId, Guid employeeId)
    {
        _provider = provider;
        _httpContextAccessor = httpContextAccessor;
        FarmId = farmId;
        AnimalId = animalId;
        EmployeeId = employeeId;
    }

    public Guid FarmId { get; }
    public Guid AnimalId { get; }
    public Guid EmployeeId { get; }

    public static SyncHarness Create(IAttendanceService? attendance = null)
    {
        var services = new ServiceCollection();
        var databaseName = $"sync-tests-{Guid.NewGuid()}";
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddDbContext<FmsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IFileStorageService>(
            new FileStorageService(Path.Combine(Path.GetTempPath(), "fms-sync-unit-uploads")));
        services.AddScoped<IAnimalService, AnimalService>();
        services.AddScoped<IFarmTaskService, FarmTaskService>();
        services.AddScoped<IAttendanceService>(provider => attendance ?? new AttendanceService(
            provider.GetRequiredService<FmsDbContext>(),
            provider.GetRequiredService<ICurrentUserService>()));
        services.AddScoped<IMutationSyncService, MutationSyncService>();

        var providerBuilt = services.BuildServiceProvider();

        var farmId = Guid.NewGuid();
        var animalId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        using (var scope = providerBuilt.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            var account = new Account { Id = Guid.NewGuid(), Name = "Harness Account" };
            var farm = new Farm { Id = farmId, AccountId = account.Id, Name = "Harness Farm" };
            var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farmId, Name = "Cattle" };
            var status = new AnimalStatus
            {
                Id = Guid.NewGuid(), FarmId = farmId, Name = "Active",
                Category = AnimalStatusCategory.Active, IsSystemDefined = true
            };

            db.Accounts.Add(account);
            db.Farms.Add(farm);
            db.AnimalTypes.Add(animalType);
            db.AnimalStatuses.Add(status);
            db.Animals.Add(new Animal
            {
                Id = animalId, FarmId = farmId, TagNumber = "H-001", Name = "Harness Cow",
                AnimalType = animalType, AnimalStatus = status, DateOfBirth = DateTime.UtcNow.AddYears(-2)
            });
            db.Employees.Add(new Employee
            {
                Id = employeeId, FarmId = farmId, FirstName = "Harness", LastName = "Worker",
                SalaryType = SalaryType.Monthly, SalaryRate = 1000m, IsActive = true
            });
            db.SaveChanges();
        }

        accessor.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "FarmManager")
            },
            "Test"));

        return new SyncHarness(providerBuilt, accessor, farmId, animalId, employeeId);
    }

    public SyncMutationItem RawItem(string operation, Guid mutationId, object payload) => new()
    {
        Operation = operation,
        ClientMutationId = mutationId,
        Payload = JsonSerializer.SerializeToElement(payload, WebOptions)
    };

    public SyncMutationItem WeightItem(Guid mutationId, decimal weightKg) =>
        RawItem(SyncOperations.WeightRecord, mutationId, new { animalId = AnimalId, weightKg });

    public SyncMutationItem CheckInItem(Guid mutationId) =>
        RawItem(SyncOperations.AttendanceCheckIn, mutationId, new { employeeId = EmployeeId });

    public async Task<SyncMutationResultDto> ApplyAsync(params SyncMutationItem[] items)
    {
        using var scope = _provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMutationSyncService>();
        return await service.ApplyAsync(FarmId, new SyncMutationRequest { Items = items.ToList() });
    }

    public int LedgerCount() => WithDb(db => db.ProcessedMutations.Count());

    public int LedgerCountFor(Guid mutationId) =>
        WithDb(db => db.ProcessedMutations.Count(m => m.ClientMutationId == mutationId));

    public int WeightCount() => WithDb(db => db.WeightRecords.Count());

    public decimal Weight() => WithDb(db => db.WeightRecords.Single().WeightKg);

    public int AttendanceCount() => WithDb(db => db.AttendanceRecords.Count());

    private T WithDb<T>(Func<FmsDbContext, T> query)
    {
        using var scope = _provider.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<FmsDbContext>());
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>A workflow that fails hard, to prove one item's exception is isolated to that item.</summary>
internal sealed class ThrowingAttendanceService : IAttendanceService
{
    public Task<Result<AttendanceRecordDto>> CheckInAsync(
        Guid farmId, Guid employeeId, CheckInRequest? request = null, Guid? mutationId = null) =>
        throw new InvalidOperationException("boom");

    public Task<Result<AttendanceRecordDto>> CheckOutAsync(
        Guid farmId, Guid employeeId, CheckOutRequest? request = null, Guid? mutationId = null) =>
        throw new NotSupportedException();

    public Task<Result<AttendanceRecordDto>> UpsertAttendanceAsync(Guid farmId, UpsertAttendanceRequest request) =>
        throw new NotSupportedException();

    public Task<Result<PagedResult<AttendanceRecordDto>>> GetAttendanceAsync(
        Guid farmId, AttendanceListFilter filter) =>
        throw new NotSupportedException();

    public Task<Result> DeleteAttendanceAsync(Guid farmId, Guid id) => throw new NotSupportedException();
}
