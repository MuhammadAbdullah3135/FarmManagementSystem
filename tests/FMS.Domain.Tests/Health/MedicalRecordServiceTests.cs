using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Enums;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

public class MedicalRecordServiceTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MedicalRecordService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(Guid FarmId, Guid AnimalId, Guid OtherAnimalId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new Domain.Entities.AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new Domain.Entities.SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new Domain.Entities.AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };

        var animal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-001",
            Name = "Bella",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        };
        var otherAnimal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-002",
            Name = "Max",
            AnimalTypeId = type.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.AddRange(animal, otherAnimal);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, animal.Id, otherAnimal.Id);
    }

    private static CreateMedicalRecordRequest ValidCreate(SeedData seed) => new()
    {
        AnimalId = seed.AnimalId,
        Symptoms = "Fever and lethargy",
        Diagnosis = "Mastitis",
        Treatment = "Antibiotics",
        VetName = "Dr. Smith",
        Cost = 150m,
        DateRecorded = DateTime.UtcNow.Date.AddDays(-3),
        Status = MedicalRecordStatus.Open
    };

    [Fact]
    public async Task CreateMedicalRecord_Succeeds_AndListsWithTag()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateMedicalRecordAsync(seed.FarmId, ValidCreate(seed));

        Assert.True(result.IsSuccess);
        Assert.Equal("TAG-001", result.Value!.AnimalTagNumber);
        Assert.Equal("Bella", result.Value.AnimalName);
        Assert.Equal("Mastitis", result.Value.Diagnosis);
        Assert.Equal(MedicalRecordStatus.Open, result.Value.Status);

        var list = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter());
        Assert.True(list.IsSuccess);
        Assert.Single(list.Value!.Items);
        Assert.Equal("Dr. Smith", list.Value.Items[0].VetName);
    }

    [Fact]
    public async Task CreateMedicalRecord_CostCreatesAutoExpense_AndFollowUpCreatesTask()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var request = ValidCreate(seed);
        request.Cost = 500m;
        request.FollowUpDate = DateTime.UtcNow.AddDays(30);
        var result = await service.CreateMedicalRecordAsync(seed.FarmId, request);

        Assert.True(result.IsSuccess);

        // Auto-created expense with a "Veterinary" category.
        var expense = await context.Expenses.SingleAsync();
        Assert.Equal(500m, expense.Amount);
        Assert.Contains("Medical:", expense.Description);
        Assert.NotNull(result.Value!.FollowUpTaskId);

        // Auto-generated follow-up task.
        var task = await context.FarmTasks.SingleAsync();
        Assert.Contains("Follow-up:", task.Title);
        Assert.Equal(seed.AnimalId, task.AnimalId);
    }

    [Fact]
    public async Task CreateMedicalRecord_MissingAnimal_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var request = ValidCreate(seed);
        request.AnimalId = Guid.NewGuid();
        var result = await service.CreateMedicalRecordAsync(seed.FarmId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task CreateMedicalRecord_EmptySymptoms_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var request = ValidCreate(seed);
        request.Symptoms = "  ";
        var result = await service.CreateMedicalRecordAsync(seed.FarmId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task GetMedicalRecords_AppliesFilters()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // Bella - resolved, older, vet A
        var a = ValidCreate(seed);
        a.Diagnosis = "Mastitis";
        a.VetName = "Dr. Alpha";
        a.Status = MedicalRecordStatus.Resolved;
        a.DateRecorded = DateTime.UtcNow.Date.AddDays(-10);
        await service.CreateMedicalRecordAsync(seed.FarmId, a);

        // Max - open, recent, vet B
        var b = ValidCreate(seed);
        b.AnimalId = seed.OtherAnimalId;
        b.Diagnosis = "Injury";
        b.VetName = "Dr. Beta";
        b.Status = MedicalRecordStatus.Open;
        b.DateRecorded = DateTime.UtcNow.Date.AddDays(-2);
        await service.CreateMedicalRecordAsync(seed.FarmId, b);

        // By animal
        var byAnimal = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter { AnimalId = seed.OtherAnimalId });
        Assert.Single(byAnimal.Value!.Items);
        Assert.Equal("Injury", byAnimal.Value.Items[0].Diagnosis);

        // By status
        var byStatus = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter { Status = MedicalRecordStatus.Resolved });
        Assert.Single(byStatus.Value!.Items);
        Assert.Equal("Mastitis", byStatus.Value.Items[0].Diagnosis);

        // By search (vet name / diagnosis / tag)
        var bySearch = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter { Search = "Beta" });
        Assert.Single(bySearch.Value!.Items);
        var byTag = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter { Search = "TAG-001" });
        Assert.Single(byTag.Value!.Items);

        // By date range
        var byDate = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter
        {
            FromDate = DateTime.UtcNow.Date.AddDays(-5),
            ToDate = DateTime.UtcNow.Date
        });
        Assert.Single(byDate.Value!.Items);
        Assert.Equal("Injury", byDate.Value.Items[0].Diagnosis);
    }

    [Fact]
    public async Task GetMedicalRecords_OrderedNewestFirst()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var old = ValidCreate(seed);
        old.DateRecorded = DateTime.UtcNow.Date.AddDays(-30);
        await service.CreateMedicalRecordAsync(seed.FarmId, old);

        var recent = ValidCreate(seed);
        recent.DateRecorded = DateTime.UtcNow.Date.AddDays(-1);
        await service.CreateMedicalRecordAsync(seed.FarmId, recent);

        var list = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter());
        Assert.Equal(recent.DateRecorded, list.Value!.Items[0].DateRecorded);
        Assert.Equal(old.DateRecorded, list.Value.Items[1].DateRecorded);
    }

    [Fact]
    public async Task GetByIdMissing_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.GetMedicalRecordByIdAsync(seed.FarmId, Guid.NewGuid());
        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateMedicalRecord_Succeeds_AndReflectsChanges()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateMedicalRecordAsync(seed.FarmId, ValidCreate(seed));

        var updated = await service.UpdateMedicalRecordAsync(seed.FarmId, created.Value!.Id, new UpdateMedicalRecordRequest
        {
            AnimalId = seed.OtherAnimalId,
            Symptoms = "Difficulty breathing",
            Diagnosis = "Pneumonia",
            VetName = "Dr. Gamma",
            Cost = 300m,
            DateRecorded = DateTime.UtcNow.Date,
            Status = MedicalRecordStatus.InProgress
        });

        Assert.True(updated.IsSuccess);
        Assert.Equal("Max", updated.Value!.AnimalName);
        Assert.Equal("Pneumonia", updated.Value.Diagnosis);
        Assert.Equal("Dr. Gamma", updated.Value.VetName);
        Assert.Equal(MedicalRecordStatus.InProgress, updated.Value.Status);

        // old animal should now have no records
        var oldAnimalRecords = await service.GetMedicalRecordsByAnimalAsync(seed.FarmId, seed.AnimalId, 1, 20);
        Assert.Empty(oldAnimalRecords.Value!.Items);
    }

    [Fact]
    public async Task UpdateMedicalRecord_MissingRecord_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.UpdateMedicalRecordAsync(seed.FarmId, Guid.NewGuid(), new UpdateMedicalRecordRequest
        {
            AnimalId = seed.AnimalId,
            Symptoms = "x",
            DateRecorded = DateTime.UtcNow.Date
        });

        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteMedicalRecord_Succeeds_AndWrongFarmReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateMedicalRecordAsync(seed.FarmId, ValidCreate(seed));

        var deleted = await service.DeleteMedicalRecordAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleted.IsSuccess);

        var list = await service.GetMedicalRecordsAsync(seed.FarmId, new MedicalRecordListFilter());
        Assert.Empty(list.Value!.Items);

        var wrongFarm = await service.DeleteMedicalRecordAsync(Guid.NewGuid(), created.Value.Id);
        Assert.Equal("NotFound", wrongFarm.Error!.Code);
    }
}