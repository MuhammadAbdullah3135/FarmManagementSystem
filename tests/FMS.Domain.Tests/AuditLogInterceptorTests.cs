using System.Security.Claims;
using System.Text.Json;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class AuditLogInterceptorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static IHttpContextAccessor CreateHttpContextAccessor(Guid? userId = null, string? email = null)
    {
        var httpContextAccessor = new HttpContextAccessor();
        if (userId.HasValue || email != null)
        {
            var claims = new List<Claim>();
            if (userId.HasValue)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
            if (email != null)
                claims.Add(new Claim(ClaimTypes.Email, email));
            var identity = new ClaimsIdentity(claims, "TestAuth");
            httpContextAccessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        }
        return httpContextAccessor;
    }

    private static FmsDbContext CreateContextWithInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditLogInterceptor(httpContextAccessor))
            .Options;
        return new FmsDbContext(options);
    }

    private static async Task<Farm> SeedFarmAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Test Farm" };
        context.Farms.Add(farm);
        await context.SaveChangesAsync();
        // Clear seed audit logs so tests can assert on their own entity only
        context.AuditLogs.RemoveRange(context.AuditLogs);
        await context.SaveChangesAsync();
        return farm;
    }

    [Fact]
    public async Task CreateEntity_GeneratesAuditLog()
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateHttpContextAccessor(userId, "test@example.com");
        using var context = CreateContextWithInterceptor(httpContext);
        var farm = await SeedFarmAsync(context);

        var cat = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        context.ExpenseCategories.Add(cat);
        await context.SaveChangesAsync();

        var logs = await context.AuditLogs.ToListAsync();
        Assert.Single(logs);
        Assert.Equal(AuditAction.Create, logs[0].Action);
        Assert.Equal("ExpenseCategory", logs[0].EntityType);
        Assert.Equal(userId, logs[0].UserId);
        Assert.Equal("test@example.com", logs[0].UserEmail);
    }

    [Fact]
    public async Task UpdateEntity_GeneratesOldAndNewValues()
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateHttpContextAccessor(userId, "test@example.com");
        using var context = CreateContextWithInterceptor(httpContext);
        var farm = await SeedFarmAsync(context);

        var cat = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        context.ExpenseCategories.Add(cat);
        await context.SaveChangesAsync();
        context.AuditLogs.RemoveRange(context.AuditLogs);
        await context.SaveChangesAsync();

        cat.Name = "Medicine";
        cat.Description = "Updated";
        await context.SaveChangesAsync();

        var logs = await context.AuditLogs.ToListAsync();
        Assert.Single(logs);
        Assert.Equal(AuditAction.Update, logs[0].Action);
        Assert.NotNull(logs[0].OldValues);
        Assert.NotNull(logs[0].NewValues);
        Assert.Contains("Feed", logs[0].OldValues);
        Assert.Contains("Medicine", logs[0].NewValues);
    }

    [Fact]
    public async Task MultiEntitySave_NoDuplicatesNoInfiniteLoop()
    {
        var userId = Guid.NewGuid();
        var httpContext = CreateHttpContextAccessor(userId, "test@example.com");
        using var context = CreateContextWithInterceptor(httpContext);
        var farm = await SeedFarmAsync(context);

        var cat = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        var pm = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };
        context.ExpenseCategories.Add(cat);
        context.PaymentMethods.Add(pm);
        await context.SaveChangesAsync();
        context.AuditLogs.RemoveRange(context.AuditLogs);
        await context.SaveChangesAsync();

        var e1 = new Expense { Id = Guid.NewGuid(), FarmId = farm.Id, Amount = 100m, ExpenseCategoryId = cat.Id, PaymentMethodId = pm.Id, ExpenseDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        var e2 = new Expense { Id = Guid.NewGuid(), FarmId = farm.Id, Amount = 50m, ExpenseCategoryId = cat.Id, PaymentMethodId = pm.Id, ExpenseDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        context.Expenses.Add(e1);
        context.Expenses.Add(e2);
        await context.SaveChangesAsync();

        var logs = await context.AuditLogs.ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal(AuditAction.Create, l.Action));
        Assert.All(logs, l => Assert.Equal("Expense", l.EntityType));
    }

    [Fact]
    public async Task NoHttpContext_DoesNotCrash()
    {
        var httpContext = CreateHttpContextAccessor();
        using var context = CreateContextWithInterceptor(httpContext);
        var farm = await SeedFarmAsync(context);

        var cat = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        context.ExpenseCategories.Add(cat);
        await context.SaveChangesAsync();

        var logs = await context.AuditLogs.ToListAsync();
        Assert.Single(logs);
        Assert.Null(logs[0].UserId);
        Assert.Null(logs[0].UserEmail);
    }

    [Fact]
    public async Task NoChanges_NoAuditEntries()
    {
        var httpContext = CreateHttpContextAccessor(Guid.NewGuid());
        using var context = CreateContextWithInterceptor(httpContext);
        await context.SaveChangesAsync();

        var logs = await context.AuditLogs.ToListAsync();
        Assert.Empty(logs);
    }
}
