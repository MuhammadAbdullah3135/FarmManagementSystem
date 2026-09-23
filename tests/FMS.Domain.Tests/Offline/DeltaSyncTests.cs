using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Application.Health;
using FMS.Application.Tasks;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Offline;

/// <summary>
/// The delta reads behind the device's cache (Phase 5.6).
///
/// <para>
/// A delta has to answer three questions correctly or a device keeps showing something that
/// is not true: what changed, what was removed, and — when it cannot tell — "ask me again for
/// everything". These tests assert each of the three per collection, including the two cases
/// that a naive "ModifiedAt &gt; cursor" filter gets wrong: a row that was created and never
/// edited (no <c>ModifiedAt</c> at all), and a row that was deleted.
/// </para>
///
/// <para>
/// Hard deletes are seeded with an audit row by hand. In production <c>AuditLogInterceptor</c>
/// writes that row; a test context is built without interceptors, so a test that wants a
/// tombstone to exist has to write the same row the interceptor would.
/// </para>
/// </summary>
public class DeltaSyncTests
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

    private sealed record Seed(Guid FarmId, Guid AnimalTypeId, Guid BreedId, Guid AnimalId, Guid OtherAnimalId);

    private static async Task<Seed> SeedFarmAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Delta Farm" };
        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cattle" };
        var breed = new Breed { Id = Guid.NewGuid(), AnimalTypeId = type.Id, Name = "Holstein" };
        var category = new AgeCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Adult", MinDays = 300, MaxDays = 4000 };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };

        var animal = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "D-001", Name = "Bella",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id,
            AgeCategoryId = category.Id
        };
        var other = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "D-002", Name = "Max",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id,
            AgeCategoryId = category.Id
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.AgeCategories.Add(category);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.AddRange(animal, other);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, type.Id, breed.Id, animal.Id, other.Id);
    }

    private static FarmTask BuildTask(Guid farmId, string title) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = farmId,
        Title = title,
        Priority = FarmTaskPriority.Medium,
        Status = FarmTaskStatus.Pending,
        DueDate = DateTime.UtcNow.AddDays(3)
    };

    /// <summary>The audit row a hard delete leaves behind — written by the interceptor in production.</summary>
    private static AuditLog DeleteRow(Guid farmId, string entityType, Guid id, DateTime? at = null) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = farmId,
        EntityType = entityType,
        EntityId = id.ToString(),
        Action = AuditAction.Delete,
        Timestamp = at ?? DateTime.UtcNow
    };

    private static AuditLog UpdateRow(Guid farmId, string entityType, Guid id, DateTime? at = null) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = farmId,
        EntityType = entityType,
        EntityId = id.ToString(),
        Action = AuditAction.Update,
        Timestamp = at ?? DateTime.UtcNow
    };

    /// <summary>
    /// A modification has to land strictly after the cursor the server handed out, or the
    /// comparison in the delta is a coin toss on the same clock tick.
    /// </summary>
    private static Task TickAsync() => Task.Delay(15);

    // ── tasks ──────────────────────────────────────────────

    [Fact]
    public async Task TaskDelta_ReturnsOnlyWhatChanged_AndNamesWhatWasDeleted()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new FarmTaskService(context, new FixedCurrentUser());

        var keep = BuildTask(seed.FarmId, "Vaccinate herd");
        var edited = BuildTask(seed.FarmId, "Fix fence");
        var removed = BuildTask(seed.FarmId, "Order feed");
        context.FarmTasks.AddRange(keep, edited, removed);
        await context.SaveChangesAsync();

        var full = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter());
        var cursor = full.Value!.Cursor;

        await TickAsync();

        edited.Title = "Fix the north fence";
        context.Remove(removed);
        context.FarmTasks.Update(edited);
        context.AuditLogs.Add(DeleteRow(seed.FarmId, nameof(FarmTask), removed.Id));
        await context.SaveChangesAsync();

        var delta = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter { UpdatedSince = cursor });

        Assert.True(delta.IsSuccess);
        Assert.False(delta.Value!.RequiresFullSync);
        Assert.Equal(new[] { "Fix the north fence" }, delta.Value.Items.Select(t => t.Title).ToArray());
        Assert.Equal(new[] { removed.Id }, delta.Value.DeletedIds.ToArray());
        // The cursor moves forward on every read, so the client always has a fresh one to send.
        Assert.True(delta.Value.Cursor > cursor);
    }

    [Fact]
    public async Task TaskDelta_SeesATaskThatWasNeverEdited()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new FarmTaskService(context, new FixedCurrentUser());

        context.FarmTasks.Add(BuildTask(seed.FarmId, "Existing"));
        await context.SaveChangesAsync();

        var full = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter());
        var cursor = full.Value!.Cursor;

        await TickAsync();

        var brandNew = BuildTask(seed.FarmId, "Brand new");
        context.FarmTasks.Add(brandNew);
        await context.SaveChangesAsync();

        // The premise: a row that was created and never edited has no ModifiedAt at all, so a
        // filter on ModifiedAt alone would leave it invisible to every delta forever.
        var stored = await context.FarmTasks.AsNoTracking().SingleAsync(t => t.Id == brandNew.Id);
        Assert.Null(stored.ModifiedAt);
        Assert.True(stored.CreatedAt > cursor);

        var delta = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter { UpdatedSince = cursor });

        Assert.Equal(new[] { "Brand new" }, delta.Value!.Items.Select(t => t.Title).ToArray());
    }

    [Theory]
    [InlineData(-40)] // older than DeltaCursor.MaxAgeDays
    [InlineData(1)]   // a device whose clock ran the other way
    public async Task TaskDelta_RefusesACursorItCannotAnswer(int dayOffset)
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new FarmTaskService(context, new FixedCurrentUser());

        context.FarmTasks.Add(BuildTask(seed.FarmId, "Untouched"));
        await context.SaveChangesAsync();

        var delta = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter
        {
            UpdatedSince = DateTime.UtcNow.AddDays(dayOffset)
        });

        // The one reply that loses data silently is "nothing changed", so an unanswerable
        // cursor is always the demand for a full read.
        Assert.True(delta.Value!.RequiresFullSync);
        Assert.Empty(delta.Value.Items);
        Assert.Empty(delta.Value.DeletedIds);
    }

    [Fact]
    public async Task TaskDelta_TooBigToBeADelta_AsksForAFullRead()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new FarmTaskService(context, new FixedCurrentUser());

        var full = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter());
        var cursor = full.Value!.Cursor;

        await TickAsync();

        for (var i = 0; i <= DeltaCursor.MaxDeltaRows; i++)
            context.FarmTasks.Add(BuildTask(seed.FarmId, $"Task {i}"));

        await context.SaveChangesAsync();

        var delta = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter { UpdatedSince = cursor });

        Assert.True(delta.Value!.RequiresFullSync);
    }

    // ── employees ──────────────────────────────────────────

    private static Employee BuildEmployee(Guid farmId, string first, string last) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = farmId,
        FirstName = first,
        LastName = last,
        SalaryType = SalaryType.Monthly,
        SalaryRate = 1000m
    };

    [Fact]
    public async Task EmployeeDelta_TombstonesASoftDelete_AndLeavesItOutOfTheItems()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new EmployeeService(context, new FixedCurrentUser());

        var stays = BuildEmployee(seed.FarmId, "Ada", "Okafor");
        var leaves = BuildEmployee(seed.FarmId, "Ben", "Silva");
        context.Employees.AddRange(stays, leaves);
        await context.SaveChangesAsync();

        var full = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter());
        var cursor = full.Value!.Cursor;

        await TickAsync();

        // The real service path: it sets DeletedAt/DeletedBy for its own reasons, but the point
        // here is that the row's modification is *visible* to a delta without the read knowing
        // anything about how an employee is removed.
        var deleteResult = await service.DeleteEmployeeAsync(seed.FarmId, leaves.Id);
        Assert.True(deleteResult.IsSuccess);

        var delta = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter { UpdatedSince = cursor });

        Assert.False(delta.Value!.RequiresFullSync);
        Assert.Empty(delta.Value.Items);
        Assert.Equal(new[] { leaves.Id }, delta.Value.DeletedIds.ToArray());
    }

    [Fact]
    public async Task EmployeeDelta_TombstonesAHardDeleteFromTheAuditLog()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new EmployeeService(context, new FixedCurrentUser());

        var gone = BuildEmployee(seed.FarmId, "Cara", "Mensah");
        context.Employees.Add(gone);
        await context.SaveChangesAsync();

        var full = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter());
        var cursor = full.Value!.Cursor;

        await TickAsync();

        // A hard delete leaves nothing to compare a timestamp against — the audit entry is the
        // only thing that can still name the row.
        context.Employees.Remove(gone);
        context.AuditLogs.Add(DeleteRow(seed.FarmId, nameof(Employee), gone.Id));
        await context.SaveChangesAsync();

        var delta = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter { UpdatedSince = cursor });

        Assert.Equal(new[] { gone.Id }, delta.Value!.DeletedIds.ToArray());
        Assert.Empty(delta.Value.Items);
    }

    [Fact]
    public async Task EmployeeDelta_SeesAnEmployeeThatWasNeverEdited()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new EmployeeService(context, new FixedCurrentUser());

        var full = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter());
        var cursor = full.Value!.Cursor;

        await TickAsync();

        var hired = BuildEmployee(seed.FarmId, "Dara", "Nwosu");
        context.Employees.Add(hired);
        await context.SaveChangesAsync();

        var stored = await context.Employees.AsNoTracking().SingleAsync(e => e.Id == hired.Id);
        Assert.Null(stored.ModifiedAt);

        var delta = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter { UpdatedSince = cursor });

        Assert.Equal(new[] { hired.Id }, delta.Value!.Items.Select(e => e.Id).ToArray());
    }

    // ── weight-check status (a computed projection) ────────

    private static WeightCheckSchedule BuildSchedule(Seed seed) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = seed.FarmId,
        AnimalTypeId = seed.AnimalTypeId,
        RecurrenceDays = 30,
        IsActive = true
    };

    [Fact]
    public async Task WeightCheckDelta_ReturnsOnlyTheEntryWhoseInputsChanged()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new WeightCheckScheduleService(context, new FixedCurrentUser());

        context.WeightCheckSchedules.Add(BuildSchedule(seed));
        await context.SaveChangesAsync();

        var full = await service.GetWeightCheckStatusAsync(seed.FarmId, null);
        var cursor = full.Value!.Cursor;
        Assert.Equal(2, full.Value.Items.Count);

        await TickAsync();

        context.WeightRecords.Add(new WeightRecord
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            AnimalId = seed.AnimalId,
            WeightKg = 410m,
            RecordedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var delta = await service.GetWeightCheckStatusAsync(seed.FarmId, cursor);

        Assert.False(delta.Value!.RequiresFullSync);
        Assert.Equal(new[] { seed.AnimalId }, delta.Value.Items.Select(s => s.AnimalId).ToArray());
    }

    [Fact]
    public async Task WeightCheckDelta_NamesADeletedAnimal_AndDropsItsEntry()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new WeightCheckScheduleService(context, new FixedCurrentUser());

        context.WeightCheckSchedules.Add(BuildSchedule(seed));
        await context.SaveChangesAsync();

        var cursor = (await service.GetWeightCheckStatusAsync(seed.FarmId, null)).Value!.Cursor;

        await TickAsync();

        var other = await context.Animals.SingleAsync(a => a.Id == seed.OtherAnimalId);
        other.IsDeleted = true;
        await context.SaveChangesAsync();

        var delta = await service.GetWeightCheckStatusAsync(seed.FarmId, cursor);

        Assert.False(delta.Value!.RequiresFullSync);
        Assert.DoesNotContain(seed.OtherAnimalId, delta.Value.Items.Select(s => s.AnimalId));
        Assert.Contains(seed.OtherAnimalId, delta.Value.DeletedIds);
    }

    [Fact]
    public async Task WeightCheckDelta_AsksForAFullReadWhenAScheduleChanged()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new WeightCheckScheduleService(context, new FixedCurrentUser());

        var schedule = BuildSchedule(seed);
        context.WeightCheckSchedules.Add(schedule);
        await context.SaveChangesAsync();

        var cursor = (await service.GetWeightCheckStatusAsync(seed.FarmId, null)).Value!.Cursor;

        await TickAsync();

        // Deactivating a schedule only removes entries: there is no id to tombstone, and the
        // changed-input filter cannot see an entry that is no longer computed at all.
        schedule.IsActive = false;
        context.AuditLogs.Add(UpdateRow(seed.FarmId, nameof(WeightCheckSchedule), schedule.Id));
        await context.SaveChangesAsync();

        var delta = await service.GetWeightCheckStatusAsync(seed.FarmId, cursor);

        Assert.True(delta.Value!.RequiresFullSync);
    }

    [Fact]
    public async Task WeightCheckStatus_NeverListsADeletedAnimal()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new WeightCheckScheduleService(context, new FixedCurrentUser());

        context.WeightCheckSchedules.Add(BuildSchedule(seed));
        await context.SaveChangesAsync();

        var animal = await context.Animals.SingleAsync(a => a.Id == seed.OtherAnimalId);
        animal.IsDeleted = true;
        await context.SaveChangesAsync();

        var full = await service.GetWeightCheckStatusAsync(seed.FarmId, null);

        // Before 5.6 the projection listed every animal on the farm, deleted ones included, and
        // created a weight-check task for each on every read — work nobody can do, pointing at
        // an animal that is gone.
        var animalIds = full.Value!.Items.Select(s => s.AnimalId).ToList();
        var taskAnimalIds = await context.FarmTasks.AsNoTracking().Select(t => t.AnimalId).ToListAsync();

        Assert.DoesNotContain(seed.OtherAnimalId, animalIds);
        Assert.Contains(seed.AnimalId, animalIds);
        Assert.DoesNotContain(seed.OtherAnimalId, taskAnimalIds);
    }

    [Fact]
    public async Task WeightCheckDelta_RefusesACursorItCannotAnswer()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new WeightCheckScheduleService(context, new FixedCurrentUser());

        context.WeightCheckSchedules.Add(BuildSchedule(seed));
        await context.SaveChangesAsync();

        var delta = await service.GetWeightCheckStatusAsync(seed.FarmId, DateTime.UtcNow.AddDays(-41));

        Assert.True(delta.Value!.RequiresFullSync);
        Assert.Empty(delta.Value.Items);
    }

    [Fact]
    public async Task WeightCheckFullRead_StillReturnsEveryEntry()
    {
        using var context = CreateContext();
        var seed = await SeedFarmAsync(context);
        var service = new WeightCheckScheduleService(context, new FixedCurrentUser());

        context.WeightCheckSchedules.Add(BuildSchedule(seed));

        // Both animals are well past due, so the collection is also the interesting case for the
        // overdue filter: a full read and a filtered read both see every entry.
        var longAgo = DateTime.UtcNow.AddDays(-100);
        foreach (var animalId in new[] { seed.AnimalId, seed.OtherAnimalId })
        {
            context.WeightRecords.Add(new WeightRecord
            {
                Id = Guid.NewGuid(),
                FarmId = seed.FarmId,
                AnimalId = animalId,
                WeightKg = 380m,
                RecordedAt = longAgo
            });
        }

        await context.SaveChangesAsync();

        // The dashboard and the notification job read the projection through the list overload;
        // it must keep returning every entry rather than a delta's worth of them.
        var all = await service.GetWeightCheckStatusAsync(seed.FarmId);
        var overdue = await service.GetOverdueWeightChecksAsync(seed.FarmId);

        Assert.Equal(2, all.Value!.Count);
        Assert.Equal(2, overdue.Value!.Count);
        Assert.All(all.Value, s => Assert.Equal(WeightCheckStatusType.Overdue, s.Status));
    }
}
