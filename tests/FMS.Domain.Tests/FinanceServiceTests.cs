using FMS.Application.Common;
using FMS.Application.Finance;
using FMS.Domain.Entities;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class FinanceServiceTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static FinanceService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(
        Guid FarmId,
        Guid FeedCategoryId,
        Guid MedicineCategoryId,
        Guid CashMethodId,
        Guid BankMethodId,
        Guid AnimalId,
        Guid LocationId,
        Guid CropCategoryId,
        Guid LivestockCategoryId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var feed = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Feed" };
        var medicine = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Medicine" };
        var cash = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };
        var bank = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Bank Transfer" };
        var animal = new Animal { Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "TAG-001", Name = "Bella" };
        var location = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Main Barn" };
        var crop = new IncomeCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Crop Sales" };
        var livestock = new IncomeCategory { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Livestock Sales" };

        context.Farms.Add(farm);
        context.ExpenseCategories.AddRange(feed, medicine);
        context.PaymentMethods.AddRange(cash, bank);
        context.Animals.Add(animal);
        context.Locations.Add(location);
        context.IncomeCategories.AddRange(crop, livestock);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, feed.Id, medicine.Id, cash.Id, bank.Id, animal.Id, location.Id, crop.Id, livestock.Id);
    }

    // Expense categories

    [Fact]
    public async Task CreateExpenseCategory_Succeeds_AndLists()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateExpenseCategoryAsync(seed.FarmId,
            new CreateExpenseCategoryRequest { Name = "Veterinary", Description = "Vet visits and medicine" });

        Assert.True(created.IsSuccess);
        Assert.Equal("Veterinary", created.Value!.Name);

        var list = await service.GetExpenseCategoriesAsync(seed.FarmId);
        Assert.Equal(3, list.Value!.Count); // seeded Feed + Medicine + Veterinary
    }

    [Fact]
    public async Task CreateExpenseCategory_DuplicateName_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateExpenseCategoryAsync(seed.FarmId,
            new CreateExpenseCategoryRequest { Name = "feed" }); // case-insensitive

        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateExpenseCategory_Succeeds_ButRenameToExisting_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var renamed = await service.UpdateExpenseCategoryAsync(seed.FarmId, seed.FeedCategoryId,
            new UpdateExpenseCategoryRequest { Name = "Animal Feed", Description = "Updated" });
        Assert.True(renamed.IsSuccess);
        Assert.Equal("Animal Feed", renamed.Value!.Name);

        var conflict = await service.UpdateExpenseCategoryAsync(seed.FarmId, seed.FeedCategoryId,
            new UpdateExpenseCategoryRequest { Name = "Medicine" });
        Assert.Equal("Conflict", conflict.Error!.Code);
    }

    [Fact]
    public async Task DeleteExpenseCategory_InUse_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateExpenseAsync(seed.FarmId, new CreateExpenseRequest
        {
            Amount = 500,
            ExpenseCategoryId = seed.MedicineCategoryId,
            PaymentMethodId = seed.CashMethodId,
            Description = "Veterinary Visit"
        });

        var result = await service.DeleteExpenseCategoryAsync(seed.FarmId, seed.MedicineCategoryId);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteExpenseCategory_Unused_Succeeds_AndDisappearsFromOtherFarmScopes()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateExpenseCategoryAsync(seed.FarmId, new CreateExpenseCategoryRequest { Name = "Equipment" });
        var deleted = await service.DeleteExpenseCategoryAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleted.IsSuccess);

        // Deleting from a different farm must not touch this farm's data.
        var otherFarmDelete = await service.DeleteExpenseCategoryAsync(Guid.NewGuid(), seed.FeedCategoryId);
        Assert.Equal("NotFound", otherFarmDelete.Error!.Code);

        var list = await service.GetExpenseCategoriesAsync(seed.FarmId);
        Assert.Equal(2, list.Value!.Count);
    }

    // Payment methods

    [Fact]
    public async Task PaymentMethod_Crud_Works_WithInUseGuard()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreatePaymentMethodAsync(seed.FarmId, new CreatePaymentMethodRequest { Name = "Mobile Money" });
        Assert.True(created.IsSuccess);

        var duplicate = await service.CreatePaymentMethodAsync(seed.FarmId, new CreatePaymentMethodRequest { Name = "mobile money" });
        Assert.Equal("Conflict", duplicate.Error!.Code);

        var updated = await service.UpdatePaymentMethodAsync(seed.FarmId, created.Value!.Id,
            new UpdatePaymentMethodRequest { Name = "M-Pesa" });
        Assert.True(updated.IsSuccess);
        Assert.Equal("M-Pesa", updated.Value!.Name);

        await service.CreateExpenseAsync(seed.FarmId, new CreateExpenseRequest
        {
            Amount = 100,
            ExpenseCategoryId = seed.FeedCategoryId,
            PaymentMethodId = created.Value.Id
        });

        var inUse = await service.DeletePaymentMethodAsync(seed.FarmId, created.Value.Id);
        Assert.Equal("Conflict", inUse.Error!.Code);
    }

    // Expenses

    [Fact]
    public async Task CreateExpense_Succeeds_WithDefaultsAndMappedNames()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateExpenseAsync(seed.FarmId, new CreateExpenseRequest
        {
            Amount = 500.456m,
            ExpenseCategoryId = seed.MedicineCategoryId,
            PaymentMethodId = seed.CashMethodId,
            Description = "Veterinary Visit"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(500.46m, result.Value!.Amount); // rounded to 2dp
        Assert.Equal(DateTime.UtcNow.Date, result.Value.ExpenseDate.Date); // defaulted to today
        Assert.Equal("Medicine", result.Value.ExpenseCategoryName);
        Assert.Equal("Cash", result.Value.PaymentMethodName);
        Assert.Null(result.Value.AnimalId);
    }

    [Fact]
    public async Task CreateExpense_NonPositiveAmount_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var zero = ValidCreate(seed); zero.Amount = 0;
        var negative = ValidCreate(seed); negative.Amount = -10;

        var zeroResult = await service.CreateExpenseAsync(seed.FarmId, zero);
        var negativeResult = await service.CreateExpenseAsync(seed.FarmId, negative);

        Assert.False(zeroResult.IsSuccess);
        Assert.Equal("Validation", zeroResult.Error!.Code);
        Assert.Equal("Validation", negativeResult.Error!.Code);
    }

    [Fact]
    public async Task CreateExpense_FutureDate_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var request = ValidCreate(seed);
        request.ExpenseDate = DateTime.UtcNow.Date.AddDays(5);

        var result = await service.CreateExpenseAsync(seed.FarmId, request);

        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task CreateExpense_MissingReferences_ReturnNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var badCategory = ValidCreate(seed); badCategory.ExpenseCategoryId = Guid.NewGuid();
        var badMethod = ValidCreate(seed); badMethod.PaymentMethodId = Guid.NewGuid();
        var badAnimal = ValidCreate(seed); badAnimal.AnimalId = Guid.NewGuid();
        var badLocation = ValidCreate(seed); badLocation.LocationId = Guid.NewGuid();

        Assert.Equal("NotFound", (await service.CreateExpenseAsync(seed.FarmId, badCategory)).Error!.Code);
        Assert.Equal("NotFound", (await service.CreateExpenseAsync(seed.FarmId, badMethod)).Error!.Code);
        Assert.Equal("NotFound", (await service.CreateExpenseAsync(seed.FarmId, badAnimal)).Error!.Code);
        Assert.Equal("NotFound", (await service.CreateExpenseAsync(seed.FarmId, badLocation)).Error!.Code);
    }

    [Fact]
    public async Task CreateExpense_LinkedToAnimalAndLocation_TracesNames()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var request = ValidCreate(seed);
        request.AnimalId = seed.AnimalId;
        request.LocationId = seed.LocationId;

        var result = await service.CreateExpenseAsync(seed.FarmId, request);

        Assert.True(result.IsSuccess);
        Assert.Equal("TAG-001", result.Value!.AnimalTagNumber);
        Assert.Equal("Bella", result.Value.AnimalName);
        Assert.Equal("Main Barn", result.Value.LocationName);
    }

    [Fact]
    public async Task GetExpenses_Paged_OrderedNewestFirst()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        for (var i = 5; i >= 1; i--)
        {
            var request = ValidCreate(seed);
            request.Amount = 100 * i;
            request.ExpenseDate = DateTime.UtcNow.Date.AddDays(-i);
            request.Description = $"Expense {i}";
            await service.CreateExpenseAsync(seed.FarmId, request);
        }

        var page1 = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter { Page = 1, PageSize = 3 });
        Assert.True(page1.IsSuccess);
        Assert.Equal(5, page1.Value!.TotalCount);
        Assert.Equal(3, page1.Value.Items.Count);
        Assert.Equal(100, page1.Value.Items[0].Amount); // -1 day is newest

        var page2 = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter { Page = 2, PageSize = 3 });
        Assert.Equal(2, page2.Value!.Items.Count);
        Assert.Equal(500, page2.Value.Items[1].Amount); // oldest last (-5 days)
    }

    [Fact]
    public async Task GetExpenses_AppliesFilters()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var vetVisit = ValidCreate(seed);
        vetVisit.Amount = 500;
        vetVisit.ExpenseCategoryId = seed.MedicineCategoryId;
        vetVisit.PaymentMethodId = seed.CashMethodId;
        vetVisit.ExpenseDate = DateTime.UtcNow.Date.AddDays(-10);
        vetVisit.AnimalId = seed.AnimalId;
        vetVisit.Description = "Veterinary Visit";
        await service.CreateExpenseAsync(seed.FarmId, vetVisit);

        var feedOrder = ValidCreate(seed);
        feedOrder.Amount = 200;
        feedOrder.ExpenseCategoryId = seed.FeedCategoryId;
        feedOrder.PaymentMethodId = seed.BankMethodId;
        feedOrder.ExpenseDate = DateTime.UtcNow.Date.AddDays(-40);
        feedOrder.Description = "Bulk feed order";
        await service.CreateExpenseAsync(seed.FarmId, feedOrder);

        // Date range
        var byDate = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter
        {
            From = DateTime.UtcNow.Date.AddDays(-15),
            To = DateTime.UtcNow.Date
        });
        Assert.Equal(1, byDate.Value!.TotalCount);
        Assert.Equal(500, byDate.Value.Items.Single().Amount);

        // Category
        var byCategory = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter { ExpenseCategoryId = seed.FeedCategoryId });
        Assert.Equal(1, byCategory.Value!.TotalCount);
        Assert.Equal(200, byCategory.Value.Items.Single().Amount);

        // Payment method
        var byMethod = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter { PaymentMethodId = seed.BankMethodId });
        Assert.Equal(1, byMethod.Value!.TotalCount);
        Assert.Equal(200, byMethod.Value.Items.Single().Amount);

        // Animal
        var byAnimal = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter { AnimalId = seed.AnimalId });
        Assert.Equal(1, byAnimal.Value!.TotalCount);

        // Search
        var bySearch = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter { Search = "veterinary" });
        Assert.Equal(1, bySearch.Value!.TotalCount);
        Assert.Contains("Veterinary", bySearch.Value.Items.Single().Description!);
    }

    [Fact]
    public async Task UpdateExpense_Succeeds_AndReflectsChanges()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateExpenseAsync(seed.FarmId, ValidCreate(seed));

        var updated = await service.UpdateExpenseAsync(seed.FarmId, created.Value!.Id, new UpdateExpenseRequest
        {
            Amount = 750,
            ExpenseCategoryId = seed.FeedCategoryId,
            PaymentMethodId = seed.BankMethodId,
            ExpenseDate = DateTime.UtcNow.Date.AddDays(-1),
            AnimalId = seed.AnimalId,
            Description = "Updated description"
        });

        Assert.True(updated.IsSuccess);
        Assert.Equal(750, updated.Value!.Amount);
        Assert.Equal("Feed", updated.Value.ExpenseCategoryName);
        Assert.Equal("Bank Transfer", updated.Value.PaymentMethodName);
        Assert.Equal("TAG-001", updated.Value.AnimalTagNumber);

        var missing = await service.UpdateExpenseAsync(seed.FarmId, Guid.NewGuid(), new UpdateExpenseRequest
        {
            Amount = 1,
            ExpenseCategoryId = seed.FeedCategoryId,
            PaymentMethodId = seed.CashMethodId,
            ExpenseDate = DateTime.UtcNow.Date
        });
        Assert.Equal("NotFound", missing.Error!.Code);
    }

    [Fact]
    public async Task DeleteExpense_Succeeds_AndFreesItsCategory()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateExpenseAsync(seed.FarmId, ValidCreate(seed));
        Assert.True(created.IsSuccess);

        var deleted = await service.DeleteExpenseAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleted.IsSuccess);

        var list = await service.GetExpensesAsync(seed.FarmId, new ExpenseListFilter());
        Assert.Empty(list.Value!.Items);

        // Category no longer in use, so it can be deleted now.
        var deleteCategory = await service.DeleteExpenseCategoryAsync(seed.FarmId, seed.MedicineCategoryId);
        Assert.True(deleteCategory.IsSuccess);
    }

    [Fact]
    public async Task DeleteExpense_WrongFarmOrMissing_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateExpenseAsync(seed.FarmId, ValidCreate(seed));

        var wrongFarm = await service.DeleteExpenseAsync(Guid.NewGuid(), created.Value!.Id);
        Assert.Equal("NotFound", wrongFarm.Error!.Code);

        var unknown = await service.DeleteExpenseAsync(seed.FarmId, Guid.NewGuid());
        Assert.Equal("NotFound", unknown.Error!.Code);
    }

    private static CreateExpenseRequest ValidCreate(SeedData seed) => new()
    {
        Amount = 100,
        ExpenseCategoryId = seed.FeedCategoryId,
        PaymentMethodId = seed.CashMethodId,
        Description = "Test expense"
    };

    // Income categories

    [Fact]
    public async Task CreateIncomeCategory_Succeeds_AndLists()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateIncomeCategoryAsync(seed.FarmId,
            new CreateIncomeCategoryRequest { Name = "Dairy Sales", Description = "Income from dairy" });

        Assert.True(created.IsSuccess);
        Assert.Equal("Dairy Sales", created.Value!.Name);

        var list = await service.GetIncomeCategoriesAsync(seed.FarmId);
        Assert.Equal(3, list.Value!.Count); // seeded Crop + Livestock + Dairy Sales
    }

    [Fact]
    public async Task CreateIncomeCategory_DuplicateName_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var duplicate = await service.CreateIncomeCategoryAsync(seed.FarmId,
            new CreateIncomeCategoryRequest { Name = "Crop Sales" });

        Assert.Equal("Conflict", duplicate.Error!.Code);
    }

    [Fact]
    public async Task UpdateIncomeCategory_Succeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var updated = await service.UpdateIncomeCategoryAsync(seed.FarmId, seed.CropCategoryId,
            new UpdateIncomeCategoryRequest { Name = "Grain Sales" });

        Assert.True(updated.IsSuccess);
        Assert.Equal("Grain Sales", updated.Value!.Name);
    }

    [Fact]
    public async Task DeleteIncomeCategory_InUse_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Create an income record using the category
        await service.CreateIncomeRecordAsync(seed.FarmId, ValidIncomeCreate(seed, seed.CropCategoryId));

        var deleteResult = await service.DeleteIncomeCategoryAsync(seed.FarmId, seed.CropCategoryId);
        Assert.Equal("Conflict", deleteResult.Error!.Code);
    }

    [Fact]
    public async Task DeleteIncomeCategory_NotInUse_Succeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // CropCategoryId has no income records
        var deleteResult = await service.DeleteIncomeCategoryAsync(seed.FarmId, seed.CropCategoryId);
        Assert.True(deleteResult.IsSuccess);
    }

    // Income records

    [Fact]
    public async Task CreateIncomeRecord_Succeeds_AndFetches()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateIncomeRecordAsync(seed.FarmId, ValidIncomeCreate(seed));

        Assert.True(created.IsSuccess);
        Assert.Equal(500m, created.Value!.Amount);
        Assert.Equal(seed.CropCategoryId, created.Value.IncomeCategoryId);

        var fetched = await service.GetIncomeRecordByIdAsync(seed.FarmId, created.Value.Id);
        Assert.True(fetched.IsSuccess);
        Assert.Equal("Crop Sales", fetched.Value!.IncomeCategoryName);
    }

    [Fact]
    public async Task CreateIncomeRecord_ZeroAmount_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateIncomeRecordAsync(seed.FarmId, new CreateIncomeRecordRequest
        {
            Amount = 0,
            IncomeCategoryId = seed.CropCategoryId,
            PaymentMethodId = seed.CashMethodId
        });

        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateIncomeRecord_Succeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateIncomeRecordAsync(seed.FarmId, ValidIncomeCreate(seed));

        var updated = await service.UpdateIncomeRecordAsync(seed.FarmId, created.Value!.Id,
            new UpdateIncomeRecordRequest
            {
                IncomeDate = created.Value.IncomeDate,
                Amount = 750,
                IncomeCategoryId = seed.CropCategoryId,
                PaymentMethodId = seed.BankMethodId,
                Description = "Updated grain sale"
            });

        Assert.True(updated.IsSuccess);
        Assert.Equal(750m, updated.Value!.Amount);
        Assert.Equal("Updated grain sale", updated.Value.Description);
    }

    [Fact]
    public async Task DeleteIncomeRecord_Succeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateIncomeRecordAsync(seed.FarmId, ValidIncomeCreate(seed));

        var deleted = await service.DeleteIncomeRecordAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleted.IsSuccess);

        var list = await service.GetIncomeRecordsAsync(seed.FarmId, new IncomeRecordListFilter());
        Assert.Empty(list.Value!.Items);
    }

    [Fact]
    public async Task IncomeRecord_Filters_ByCategory()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateIncomeRecordAsync(seed.FarmId, ValidIncomeCreate(seed, seed.CropCategoryId));
        await service.CreateIncomeRecordAsync(seed.FarmId, ValidIncomeCreate(seed, seed.LivestockCategoryId));

        var filtered = await service.GetIncomeRecordsAsync(seed.FarmId,
            new IncomeRecordListFilter { IncomeCategoryId = seed.CropCategoryId });

        Assert.Single(filtered.Value!.Items);
        Assert.Equal("Crop Sales", filtered.Value.Items[0].IncomeCategoryName);
    }

    // Reports

    [Fact]
    public async Task ProfitLoss_ReturnsCorrectTotals()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateExpenseAsync(seed.FarmId, new CreateExpenseRequest
        {
            Amount = 200,
            ExpenseCategoryId = seed.FeedCategoryId,
            PaymentMethodId = seed.CashMethodId
        });
        await service.CreateIncomeRecordAsync(seed.FarmId, new CreateIncomeRecordRequest
        {
            Amount = 1000,
            IncomeCategoryId = seed.CropCategoryId,
            PaymentMethodId = seed.CashMethodId
        });

        var report = await service.GetProfitLossReportAsync(seed.FarmId, new FinanceReportFilter());

        Assert.True(report.IsSuccess);
        Assert.Equal(1000m, report.Value!.TotalIncome);
        Assert.Equal(200m, report.Value.TotalExpenses);
        Assert.Equal(800m, report.Value.NetProfit);
    }

    [Fact]
    public async Task ExpenseBreakdown_GroupsByCategory()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateExpenseAsync(seed.FarmId, new CreateExpenseRequest
        {
            Amount = 100,
            ExpenseCategoryId = seed.FeedCategoryId,
            PaymentMethodId = seed.CashMethodId
        });
        await service.CreateExpenseAsync(seed.FarmId, new CreateExpenseRequest
        {
            Amount = 50,
            ExpenseCategoryId = seed.FeedCategoryId,
            PaymentMethodId = seed.CashMethodId
        });
        await service.CreateExpenseAsync(seed.FarmId, new CreateExpenseRequest
        {
            Amount = 200,
            ExpenseCategoryId = seed.MedicineCategoryId,
            PaymentMethodId = seed.CashMethodId
        });

        var breakdown = await service.GetExpenseBreakdownAsync(seed.FarmId, new FinanceReportFilter());

        Assert.True(breakdown.IsSuccess);
        Assert.Equal(2, breakdown.Value!.Items.Count);
        Assert.Equal(350m, breakdown.Value.GrandTotal);
        Assert.Equal("Medicine", breakdown.Value.Items[0].CategoryName); // 200 is higher
        Assert.Equal("Feed", breakdown.Value.Items[1].CategoryName);
    }

    [Fact]
    public async Task IncomeBreakdown_GroupsByCategory()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateIncomeRecordAsync(seed.FarmId, new CreateIncomeRecordRequest
        {
            Amount = 500,
            IncomeCategoryId = seed.CropCategoryId,
            PaymentMethodId = seed.CashMethodId
        });
        await service.CreateIncomeRecordAsync(seed.FarmId, new CreateIncomeRecordRequest
        {
            Amount = 300,
            IncomeCategoryId = seed.LivestockCategoryId,
            PaymentMethodId = seed.CashMethodId
        });

        var breakdown = await service.GetIncomeBreakdownAsync(seed.FarmId, new FinanceReportFilter());

        Assert.True(breakdown.IsSuccess);
        Assert.Equal(2, breakdown.Value!.Items.Count);
        Assert.Equal(800m, breakdown.Value.GrandTotal);
    }

    [Fact]
    public async Task MonthlySummary_Returns12Months()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var summary = await service.GetMonthlySummaryAsync(seed.FarmId, DateTime.UtcNow.Year);

        Assert.True(summary.IsSuccess);
        Assert.Equal(12, summary.Value!.Count);
        Assert.Equal("January", summary.Value[0].MonthName);
        Assert.Equal("December", summary.Value[11].MonthName);
    }

    private static CreateIncomeRecordRequest ValidIncomeCreate(SeedData seed, Guid? categoryId = null) => new()
    {
        Amount = 500,
        IncomeCategoryId = categoryId ?? seed.CropCategoryId,
        PaymentMethodId = seed.CashMethodId,
        Description = "Test income"
    };
}
