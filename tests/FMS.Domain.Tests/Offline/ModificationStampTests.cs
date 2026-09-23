using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Offline;

/// <summary>
/// The central modification stamp (Phase 5.6).
///
/// <para>
/// Offline delta reads ask "what changed since &lt;cursor&gt;", so the correctness of every
/// cached collection rests on one guarantee: a row that was written has a timestamp saying so.
/// Before this, <c>ModifiedAt</c> was set by hand in the writing service — which is exactly how
/// a row ends up with no timestamp and therefore invisible to every device forever. These tests
/// assert the guarantee is the context's, not the caller's.
/// </para>
/// </summary>
public class ModificationStampTests
{
    private static FmsDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString());

        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);

        return new FmsDbContext(builder.Options);
    }

    private static FarmTask BuildTask(Guid farmId, string title) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = farmId,
        Title = title,
        Priority = FarmTaskPriority.Medium,
        Status = FarmTaskStatus.Pending,
        DueDate = DateTime.UtcNow.AddDays(1)
    };

    [Fact]
    public async Task Insert_StampsCreatedAt_AndPreservesADeliberateValue()
    {
        using var context = CreateContext();
        var farmId = Guid.NewGuid();

        var stamped = BuildTask(farmId, "Stamped");
        stamped.CreatedAt = default;

        var deliberate = BuildTask(farmId, "Imported");
        deliberate.CreatedAt = new DateTime(2024, 3, 1, 8, 0, 0, DateTimeKind.Utc);

        context.FarmTasks.AddRange(stamped, deliberate);
        await context.SaveChangesAsync();

        Assert.NotEqual(default, stamped.CreatedAt);
        Assert.Null(stamped.ModifiedAt);
        // An import, a seeder or a backfill states the time it is recording; the stamp must not
        // overwrite that with "now".
        Assert.Equal(new DateTime(2024, 3, 1, 8, 0, 0, DateTimeKind.Utc), deliberate.CreatedAt);
    }

    [Fact]
    public async Task Modification_StampsModifiedAt_WithoutTheCallerSettingIt()
    {
        using var context = CreateContext();
        var farmId = Guid.NewGuid();

        var task = BuildTask(farmId, "Before");
        context.FarmTasks.Add(task);
        await context.SaveChangesAsync();

        await Task.Delay(15);

        // Deliberately no `task.ModifiedAt = DateTime.UtcNow`: this is the write a service makes
        // when it forgets, and it is the write a delta depends on being visible.
        task.Status = FarmTaskStatus.InProgress;
        await context.SaveChangesAsync();

        Assert.NotNull(task.ModifiedAt);
        Assert.True(task.ModifiedAt > task.CreatedAt);
    }

    [Fact]
    public async Task SoftDelete_StampsTheTombstone_WithoutTheCallerSettingIt()
    {
        using var context = CreateContext();

        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = Guid.NewGuid(),
            TagNumber = "S-001",
            AnimalTypeId = Guid.NewGuid(),
            SexOptionId = Guid.NewGuid(),
            AnimalStatusId = Guid.NewGuid()
        };

        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        var createdAt = animal.CreatedAt;
        await Task.Delay(15);

        animal.IsDeleted = true;
        await context.SaveChangesAsync();

        // A deleted row is the one tombstone a client cannot infer from absence, so the row it
        // left behind has to move forward in time — otherwise the delta never reports it.
        Assert.NotNull(animal.ModifiedAt);
        Assert.True(animal.ModifiedAt > createdAt);
    }

    [Fact]
    public async Task WithTheAuditInterceptor_AChange_IsStampedAndAuditedExactlyOnce()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        using var context = CreateContext(new AuditLogInterceptor(accessor));
        var farmId = Guid.NewGuid();

        var task = BuildTask(farmId, "Audited");
        context.FarmTasks.Add(task);
        await context.SaveChangesAsync();

        context.AuditLogs.RemoveRange(context.AuditLogs);
        await context.SaveChangesAsync();

        await Task.Delay(15);

        task.Status = FarmTaskStatus.Completed;
        await context.SaveChangesAsync();

        // The stamp and the audit trail share one save: the interceptor's nested save must not
        // stamp a second time (which would move ModifiedAt past what the payload recorded), and
        // it must not write the change twice.
        Assert.NotNull(task.ModifiedAt);
        Assert.Equal(1, await context.AuditLogs.CountAsync());
    }
}
