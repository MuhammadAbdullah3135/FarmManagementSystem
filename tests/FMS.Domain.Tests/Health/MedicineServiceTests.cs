using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

public class MedicineServiceTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MedicineService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(Guid FarmId, Guid MedicineId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context, string medicineName = "Penicillin")
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var medicine = new Domain.Entities.Medicine
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = medicineName,
            Unit = "ml",
            LowStockThreshold = 50,
            ExpiringSoonDays = 30
        };

        context.Farms.Add(farm);
        context.Medicines.Add(medicine);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, medicine.Id);
    }

    private static AddStockBatchRequest ValidStock(string batch, int qty, DateTime? expiry = null) => new()
    {
        BatchNumber = batch,
        Quantity = qty,
        UnitCost = 10m,
        ExpiryDate = expiry ?? DateTime.UtcNow.AddMonths(6),
        Supplier = "Vet Supplier"
    };

    [Fact]
    public async Task CreateMedicine_Succeeds_AndLists()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateMedicineAsync(seed.FarmId, new CreateMedicineRequest
        {
            Name = "Oxytetracycline",
            Unit = "mg",
            LowStockThreshold = 25,
            ExpiringSoonDays = 45
        });

        Assert.True(created.IsSuccess);
        Assert.Equal("Oxytetracycline", created.Value!.Name);
        Assert.Equal(0, created.Value.TotalQuantity);

        var list = await service.GetMedicinesAsync(seed.FarmId, 1, 20, null);
        Assert.Equal(2, list.Value!.TotalCount);
    }

    [Fact]
    public async Task CreateMedicine_MissingNameOrUnit_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var noName = await service.CreateMedicineAsync(seed.FarmId, new CreateMedicineRequest { Unit = "ml" });
        var noUnit = await service.CreateMedicineAsync(seed.FarmId, new CreateMedicineRequest { Name = "Aspirin" });

        Assert.Equal("Validation", noName.Error!.Code);
        Assert.Equal("Validation", noUnit.Error!.Code);
    }

    [Fact]
    public async Task CreateMedicine_DuplicateName_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateMedicineAsync(seed.FarmId, new CreateMedicineRequest { Name = "Penicillin", Unit = "ml" });
        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateMedicine_Succeeds_AndRenameCollisionReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateMedicineAsync(seed.FarmId, new CreateMedicineRequest { Name = "Tetracycline", Unit = "ml" });

        var updated = await service.UpdateMedicineAsync(seed.FarmId, seed.MedicineId, new UpdateMedicineRequest
        {
            Name = "Penicillin G",
            Unit = "ml",
            LowStockThreshold = 80,
            ExpiringSoonDays = 60
        });
        Assert.True(updated.IsSuccess);
        Assert.Equal("Penicillin G", updated.Value!.Name);
        Assert.Equal(80, updated.Value.LowStockThreshold);

        var conflict = await service.UpdateMedicineAsync(seed.FarmId, seed.MedicineId, new UpdateMedicineRequest
        {
            Name = "Tetracycline",
            Unit = "ml"
        });
        Assert.Equal("Conflict", conflict.Error!.Code);

        var missing = await service.UpdateMedicineAsync(seed.FarmId, Guid.NewGuid(), new UpdateMedicineRequest { Name = "X", Unit = "ml" });
        Assert.Equal("NotFound", missing.Error!.Code);
    }

    [Fact]
    public async Task DeleteMedicine_WithUsage_IsBlocked_OtherwiseSucceeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var missing = await service.DeleteMedicineAsync(seed.FarmId, Guid.NewGuid());
        Assert.Equal("NotFound", missing.Error!.Code);

        // unused medicine (no stock, no usage) -> delete allowed
        var ok = await service.DeleteMedicineAsync(seed.FarmId, seed.MedicineId);
        Assert.True(ok.IsSuccess);

        // a medicine that has been used cannot be deleted
        var seed2 = await SeedAsync(context, "Vetmed");
        await service.AddStockBatchAsync(seed2.FarmId, seed2.MedicineId, ValidStock("B-2", 50));
        await service.RecordUsageAsync(seed2.FarmId, new RecordMedicineUsageRequest
        {
            MedicineId = seed2.MedicineId,
            QuantityUsed = 10
        });

        var blocked = await service.DeleteMedicineAsync(seed2.FarmId, seed2.MedicineId);
        Assert.False(blocked.IsSuccess);
        Assert.Equal("Validation", blocked.Error!.Code);
    }

    [Fact]
    public async Task AddStockBatch_EmptyBatchOrNonPositiveQty_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var noBatch = await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("", 10));
        var zeroQty = await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("B-3", 0));

        Assert.Equal("Validation", noBatch.Error!.Code);
        Assert.Equal("Validation", zeroQty.Error!.Code);

        var wrongMedicine = await service.AddStockBatchAsync(seed.FarmId, Guid.NewGuid(), ValidStock("B-4", 10));
        Assert.Equal("NotFound", wrongMedicine.Error!.Code);
    }

    [Fact]
    public async Task GetStockBatches_OrderedByExpiry()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("LATE", 10, DateTime.UtcNow.AddMonths(12)));
        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("EARLY", 10, DateTime.UtcNow.AddMonths(2)));

        var batches = await service.GetStockBatchesAsync(seed.FarmId, seed.MedicineId);
        Assert.True(batches.IsSuccess);
        Assert.Equal(2, batches.Value!.Count);
        Assert.Equal("EARLY", batches.Value[0].BatchNumber);
        Assert.Equal("LATE", batches.Value[1].BatchNumber);
    }

    [Fact]
    public async Task DeleteStockBatch_WithUsage_IsBlocked()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var stock = await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("B-5", 100));
        await service.RecordUsageAsync(seed.FarmId, new RecordMedicineUsageRequest
        {
            MedicineId = seed.MedicineId,
            QuantityUsed = 10
        });

        var blocked = await service.DeleteStockBatchAsync(seed.FarmId, seed.MedicineId, stock.Value!.Id);
        Assert.Equal("Validation", blocked.Error!.Code);
    }

    [Fact]
    public async Task RecordUsage_DeductsFifoAcrossBatches()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("F1", 10, DateTime.UtcNow.AddMonths(3)));
        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("F2", 10, DateTime.UtcNow.AddMonths(9)));

        // Use 15 -> drains F1 (10) then 5 from F2.
        var usage = await service.RecordUsageAsync(seed.FarmId, new RecordMedicineUsageRequest
        {
            MedicineId = seed.MedicineId,
            QuantityUsed = 15
        });

        Assert.True(usage.IsSuccess);
        Assert.Equal("Penicillin", usage.Value!.MedicineName);
        Assert.Equal("F2", usage.Value.BatchNumber); // linked to the last batch deducted (FIFO)

        var medicine = (await service.GetMedicineByIdAsync(seed.FarmId, seed.MedicineId)).Value!;
        Assert.Equal(5, medicine.TotalQuantity); // 10+10-15
        Assert.Equal(2, medicine.BatchCount);
    }

    [Fact]
    public async Task RecordUsage_InsufficientStock_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("F1", 5));

        var result = await service.RecordUsageAsync(seed.FarmId, new RecordMedicineUsageRequest
        {
            MedicineId = seed.MedicineId,
            QuantityUsed = 10
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Contains("Insufficient stock", result.Error!.Message);
    }

    [Fact]
    public async Task GetAlerts_LowStock_ExpiredAndExpiringSoon_AreReported()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Keep the SUM of all batches below the low-stock threshold (50) so a Low Stock
        // alert is raised alongside the expiry-based alerts.
        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("LOW", 10, DateTime.UtcNow.AddMonths(12)));   // long shelf life
        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("EXPIRE", 10, DateTime.UtcNow.AddDays(-3)));  // expired
        await service.AddStockBatchAsync(seed.FarmId, seed.MedicineId, ValidStock("SOON", 10, DateTime.UtcNow.AddDays(5)));     // expiring soon (<30d)

        var alerts = await service.GetAlertsAsync(seed.FarmId);
        Assert.True(alerts.IsSuccess);
        Assert.NotEmpty(alerts.Value!);

        var alertList = alerts.Value!;
        Assert.NotEmpty(alertList);

        var expired = alertList.Single(a => a.BatchNumber == "EXPIRE");
        Assert.Equal(MedicineAlertType.Expired, expired.AlertType);
        Assert.Equal("Expired", expired.AlertTypeName);

        var soon = alertList.Single(a => a.BatchNumber == "SOON");
        Assert.Equal(MedicineAlertType.ExpiringSoon, soon.AlertType);

        // Combined quantity (30) is below the low-stock threshold of 50.
        Assert.Contains(alertList, a => a.AlertType == MedicineAlertType.LowStock);
        Assert.Contains(alertList, a => a.CurrentQuantity == 30);
    }

    [Fact]
    public async Task GetAlerts_NoMedicine_ReturnsEmpty()
    {
        using var context = CreateContext();
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Empty Farm" };
        context.Farms.Add(farm);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var alerts = await service.GetAlertsAsync(farm.Id);
        Assert.Empty(alerts.Value!);
    }
}