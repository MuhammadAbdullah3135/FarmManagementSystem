using System.Text;
using FMS.Application.Common;
using FMS.Application.Finance;
using FMS.Domain.Entities;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

/// <summary>
/// Receipts on an expense: stored through the shared file store, reachable only through the
/// farm that owns the expense, and removed together with their file.
/// </summary>
public class ExpenseReceiptTests
{
    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
        public string? GetUserEmail() => null;
    }

    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>A fresh root per test, so one test's files are never another's.</summary>
    private static string NewUploadsRoot() =>
        Path.Combine(Path.GetTempPath(), "fms-receipt-tests", Guid.NewGuid().ToString("N"));

    private static MemoryStream FileContent() =>
        new(Encoding.UTF8.GetBytes("receipt bytes"));

    private static async Task<(Guid FarmId, Guid ExpenseId)> SeedExpenseAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Receipt farm" };
        var category = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        var method = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };

        context.Farms.Add(farm);
        context.ExpenseCategories.Add(category);
        context.PaymentMethods.Add(method);
        await context.SaveChangesAsync();

        var service = new FinanceService(context, new FixedCurrentUser(), new FileStorageService(NewUploadsRoot()));
        var created = await service.CreateExpenseAsync(farm.Id, new CreateExpenseRequest
        {
            Amount = 42.50m,
            ExpenseCategoryId = category.Id,
            PaymentMethodId = method.Id,
            Description = "Feed purchase"
        });

        Assert.True(created.IsSuccess);
        return (farm.Id, created.Value!.Id);
    }

    [Fact]
    public async Task AddReceipt_stores_the_file_and_returns_it_on_the_expense()
    {
        using var context = CreateContext();
        var (farmId, expenseId) = await SeedExpenseAsync(context);
        var storage = new FileStorageService(NewUploadsRoot());
        var service = new FinanceService(context, new FixedCurrentUser(), storage);

        using var content = FileContent();
        var added = await service.AddReceiptAsync(
            farmId, expenseId, content, "receipt.png", "image/png", content.Length, "Supplier receipt");

        Assert.True(added.IsSuccess);
        Assert.Equal("receipt.png", added.Value!.OriginalFileName);
        Assert.Contains($"/receipts/{added.Value.Id}/download", added.Value.Url);

        var stored = await context.ExpenseReceipts.SingleAsync();
        Assert.True(await storage.ExistsAsync(stored.StoragePath));

        var expense = await service.GetExpenseByIdAsync(farmId, expenseId);
        Assert.True(expense.IsSuccess);
        var only = Assert.Single(expense.Value!.Receipts);
        Assert.Equal(added.Value.Id, only.Id);
    }

    [Fact]
    public async Task AddReceipt_refuses_a_file_type_a_receipt_cannot_be()
    {
        using var context = CreateContext();
        var (farmId, expenseId) = await SeedExpenseAsync(context);
        var service = new FinanceService(context, new FixedCurrentUser(), new FileStorageService(NewUploadsRoot()));

        using var content = FileContent();
        var added = await service.AddReceiptAsync(
            farmId, expenseId, content, "notes.txt", "text/plain", content.Length, null);

        Assert.False(added.IsSuccess);
        Assert.Equal("Validation", added.Error!.Code);
        Assert.Equal(DomainMessageKeys.FileTypeNotAllowed, added.Error.MessageKey);
        Assert.Empty(context.ExpenseReceipts);
    }

    [Fact]
    public async Task AddReceipt_refuses_another_farms_expense()
    {
        using var context = CreateContext();
        var (_, expenseId) = await SeedExpenseAsync(context);
        var service = new FinanceService(context, new FixedCurrentUser(), new FileStorageService(NewUploadsRoot()));

        using var content = FileContent();
        var added = await service.AddReceiptAsync(
            Guid.NewGuid(), expenseId, content, "receipt.png", "image/png", content.Length, null);

        Assert.False(added.IsSuccess);
        Assert.Equal("NotFound", added.Error!.Code);
        Assert.Empty(context.ExpenseReceipts);
    }

    [Fact]
    public async Task DeleteReceipt_removes_the_row_and_the_stored_file()
    {
        using var context = CreateContext();
        var (farmId, expenseId) = await SeedExpenseAsync(context);
        var storage = new FileStorageService(NewUploadsRoot());
        var service = new FinanceService(context, new FixedCurrentUser(), storage);

        using var content = FileContent();
        var added = await service.AddReceiptAsync(
            farmId, expenseId, content, "receipt.png", "image/png", content.Length, null);
        Assert.True(added.IsSuccess);

        var storagePath = (await context.ExpenseReceipts.SingleAsync()).StoragePath;

        var deleted = await service.DeleteReceiptAsync(farmId, expenseId, added.Value!.Id);

        Assert.True(deleted.IsSuccess);
        Assert.Empty(context.ExpenseReceipts);
        Assert.False(await storage.ExistsAsync(storagePath));
    }

    [Fact]
    public async Task AddReceipt_refuses_more_than_the_per_expense_cap()
    {
        using var context = CreateContext();
        var (farmId, expenseId) = await SeedExpenseAsync(context);
        var service = new FinanceService(context, new FixedCurrentUser(), new FileStorageService(NewUploadsRoot()));

        for (var i = 0; i < 5; i++)
        {
            using var content = FileContent();
            var added = await service.AddReceiptAsync(
                farmId, expenseId, content, $"receipt-{i}.png", "image/png", content.Length, null);
            Assert.True(added.IsSuccess);
        }

        using var extra = FileContent();
        var refused = await service.AddReceiptAsync(
            farmId, expenseId, extra, "one-too-many.png", "image/png", extra.Length, null);

        Assert.False(refused.IsSuccess);
        Assert.Equal("Conflict", refused.Error!.Code);
        Assert.Equal(DomainMessageKeys.ExpenseReceiptLimitReached, refused.Error.MessageKey);
        Assert.Equal(5, await context.ExpenseReceipts.CountAsync());
    }
}
