using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Enums;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

/// <summary>
/// Cross-module: deleting/updating a MedicalRecord must keep its auto-created
/// linked Finance Expense consistent (not orphaned, not stale).
/// </summary>
public class MedicalRecordOrphanAndLinkTests
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

    private static MedicalRecordService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed record Seed(Guid FarmId, Guid AnimalId, Guid PaymentMethodId);

    private static async Task<Seed> SeedAsync(FmsDbContext context)
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new Domain.Entities.AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new Domain.Entities.SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new Domain.Entities.AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };
        var animal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "T1", Name = "Bella",
            AnimalTypeId = type.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id
        };
        var payMethod = new Domain.Entities.PaymentMethod { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cash" };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.Add(animal);
        context.PaymentMethods.Add(payMethod);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, animal.Id, payMethod.Id);
    }

    private static CreateMedicalRecordRequest ValidCreate(Seed seed, decimal cost = 150m) => new()
    {
        AnimalId = seed.AnimalId,
        Symptoms = "Fever",
        Diagnosis = "Mastitis",
        Cost = cost,
        DateRecorded = DateTime.UtcNow.Date.AddDays(-3),
        Status = MedicalRecordStatus.Open
    };

    [Fact]
    public async Task DeleteMedicalRecord_SoftDeletesLinkedExpense()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Create medical record with cost -> auto-creates linked expense
        var created = await service.CreateMedicalRecordAsync(seed.FarmId, ValidCreate(seed, cost: 500m));
        Assert.True(created.IsSuccess);
        var recordEntity = await context.MedicalRecords.SingleAsync(m => m.Id == created.Value!.Id);
        Assert.True(recordEntity.ExpenseId.HasValue, "expected auto-generated linked expense");

        // Confirm an expense was created
        var expenseCountBefore = await context.Expenses.CountAsync(e => e.FarmId == seed.FarmId);
        Assert.Equal(1, expenseCountBefore);

        // Delete the medical record
        var del = await service.DeleteMedicalRecordAsync(seed.FarmId, created.Value.Id);
        Assert.True(del.IsSuccess);

        var expense = await context.Expenses.SingleAsync(e => e.Id == recordEntity.ExpenseId);
        var medicalRecord = await context.MedicalRecords.SingleAsync(m => m.Id == created.Value.Id);

        Assert.True(expense.IsDeleted);
        Assert.NotNull(expense.DeletedAt);
        Assert.True(medicalRecord.IsDeleted);
        Assert.NotNull(medicalRecord.DeletedAt);
    }

    [Fact]
    public async Task UpdateMedicalRecord_ResyncsAutoExpenseCost()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateMedicalRecordAsync(seed.FarmId, ValidCreate(seed, cost: 100m));
        Assert.True(created.IsSuccess);
        var recordEntity = await context.MedicalRecords.SingleAsync(m => m.Id == created.Value!.Id);
        Assert.True(recordEntity.ExpenseId.HasValue);

        var originalExpense = await context.Expenses.SingleAsync(e => e.Id == recordEntity.ExpenseId);
        Assert.Equal(100m, originalExpense.Amount);

        // Update the medical record's cost to 800 -> linked expense should follow
        var update = await service.UpdateMedicalRecordAsync(seed.FarmId, created.Value.Id, new UpdateMedicalRecordRequest
        {
            AnimalId = seed.AnimalId,
            Symptoms = "Fever",
            Diagnosis = "Severe mastitis",
            Cost = 800m,
            DateRecorded = DateTime.UtcNow.Date.AddDays(-3),
            Status = MedicalRecordStatus.Open
        });
        Assert.True(update.IsSuccess);
        Assert.Equal(800m, update.Value!.Cost);

        var updatedExpense = await context.Expenses.SingleAsync(e => e.Id == originalExpense.Id);

        Assert.Equal(800m, updatedExpense.Amount);
    }
}
