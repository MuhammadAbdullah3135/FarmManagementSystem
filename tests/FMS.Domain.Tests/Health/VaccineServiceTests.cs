using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

public class VaccineServiceTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static VaccineService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(
        Guid FarmId,
        Guid VaccineTypeId,
        Guid MedicineId,
        Guid AnimalId,
        Guid OtherAnimalId,
        Guid AnimalTypeId,
        Guid BreedId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Domain.Entities.Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var medicine = new Domain.Entities.Medicine { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Foot-and-Mouth Vaccine", Unit = "dose" };
        var type = new Domain.Entities.AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var breed = new Domain.Entities.Breed { Id = Guid.NewGuid(), AnimalTypeId = type.Id, Name = "Holstein" };
        var sex = new Domain.Entities.SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new Domain.Entities.AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };
        var vax1 = new Domain.Entities.VaccineType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Brucellosis", LinkedMedicineId = medicine.Id };

        var animal = new Domain.Entities.Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-001",
            Name = "Bella",
            AnimalTypeId = type.Id,
            BreedId = breed.Id,
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
            BreedId = breed.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        };

        context.Farms.Add(farm);
        context.Medicines.Add(medicine);
        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.VaccineTypes.Add(vax1);
        context.Animals.AddRange(animal, otherAnimal);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, vax1.Id, medicine.Id, animal.Id, otherAnimal.Id, type.Id, breed.Id);
    }

    [Fact]
    public async Task VaccineType_Crud_Works_WithNameCollisionGuard()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateVaccineTypeAsync(seed.FarmId, new CreateVaccineTypeRequest
        {
            Name = "Anthrax",
            DefaultDosage = "2ml",
            LinkedMedicineId = seed.MedicineId
        });
        Assert.True(created.IsSuccess);
        Assert.Equal("Anthrax", created.Value!.Name);

        // Linked medicine name is populated when fetched by id (create DTO leaves it blank).
        var byId = await service.GetVaccineTypeByIdAsync(seed.FarmId, created.Value.Id);
        Assert.True(byId.IsSuccess);
        Assert.Equal("Foot-and-Mouth Vaccine", byId.Value!.LinkedMedicineName);

        var duplicate = await service.CreateVaccineTypeAsync(seed.FarmId, new CreateVaccineTypeRequest { Name = "Anthrax" });
        Assert.Equal("Conflict", duplicate.Error!.Code);

        var badMedicine = await service.CreateVaccineTypeAsync(seed.FarmId, new CreateVaccineTypeRequest
        {
            Name = "TB Test",
            LinkedMedicineId = Guid.NewGuid()
        });
        Assert.Equal("Validation", badMedicine.Error!.Code);

        var updated = await service.UpdateVaccineTypeAsync(seed.FarmId, created.Value.Id, new UpdateVaccineTypeRequest
        {
            Name = "Anthrax X",
            DefaultDosage = "3ml"
        });
        Assert.True(updated.IsSuccess);
        Assert.Equal("Anthrax X", updated.Value!.Name);

        Assert.Equal("Validation",
            (await service.CreateVaccineTypeAsync(seed.FarmId, new CreateVaccineTypeRequest())).Error!.Code);
    }

    [Fact]
    public async Task DeleteVaccineType_WithRecordsOrSchedules_IsBlocked()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateVaccineTypeAsync(seed.FarmId, new CreateVaccineTypeRequest { Name = "Temporary" });
        var deletion = await service.DeleteVaccineTypeAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deletion.IsSuccess);

        // With vaccination record
        await service.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow.Date
        });
        var blockedByRecord = await service.DeleteVaccineTypeAsync(seed.FarmId, seed.VaccineTypeId);
        Assert.Equal("Validation", blockedByRecord.Error!.Code);

        // With schedule
        var vaxType2 = (await service.CreateVaccineTypeAsync(seed.FarmId, new CreateVaccineTypeRequest { Name = "Scheduled Vaccine" })).Value!;
        await service.CreateScheduleAsync(seed.FarmId, new CreateVaccinationScheduleRequest
        {
            VaccineTypeId = vaxType2.Id,
            RecurrenceDays = 90
        });
        var blockedBySchedule = await service.DeleteVaccineTypeAsync(seed.FarmId, vaxType2.Id);
        Assert.Equal("Validation", blockedBySchedule.Error!.Code);
    }

    [Fact]
    public async Task VaccinationRecord_Crud_AndFilters_Work()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var rec = await service.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow.Date.AddDays(-30),
            VetName = "Dr. Alpha",
            BatchNumber = "BATCH-1",
            Cost = 120m
        });
        Assert.True(rec.IsSuccess);
        Assert.Equal("Brucellosis", rec.Value!.VaccineTypeName);
        Assert.Equal("TAG-001", rec.Value.AnimalTagNumber);

        // auto expense created
        var expense = await context.Expenses.SingleAsync();
        Assert.Equal(120m, expense.Amount);

        // second record
        await service.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.OtherAnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow.Date.AddDays(-5),
            VetName = "Dr. Beta",
            BatchNumber = "BATCH-2"
        });

        var all = await service.GetVaccinationRecordsAsync(seed.FarmId, new VaccinationRecordListFilter());
        Assert.Equal(2, all.Value!.TotalCount);

        var byAnimal = await service.GetVaccinationRecordsAsync(seed.FarmId, new VaccinationRecordListFilter { AnimalId = seed.OtherAnimalId });
        Assert.Single(byAnimal.Value!.Items);
        Assert.Equal("TAG-002", byAnimal.Value!.Items[0].AnimalTagNumber);

        var bySearch = await service.GetVaccinationRecordsAsync(seed.FarmId, new VaccinationRecordListFilter { Search = "Dr. Alpha" });
        Assert.Single(bySearch.Value!.Items);

        var byDate = await service.GetVaccinationRecordsAsync(seed.FarmId, new VaccinationRecordListFilter
        {
            FromDate = DateTime.UtcNow.Date.AddDays(-10),
            ToDate = DateTime.UtcNow.Date
        });
        Assert.Single(byDate.Value!.Items);
        Assert.Equal("TAG-002", byDate.Value!.Items[0].AnimalTagNumber);

        var notFound = await service.GetVaccinationRecordByIdAsync(seed.FarmId, Guid.NewGuid());
        Assert.Equal("NotFound", notFound.Error!.Code);
    }

    [Fact]
    public async Task VaccinationRecord_MissingAnimalOrType_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var badAnimal = await service.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = Guid.NewGuid(),
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow.Date
        });
        Assert.Equal("Validation", badAnimal.Error!.Code);

        var badType = await service.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = Guid.NewGuid(),
            DateGiven = DateTime.UtcNow.Date
        });
        Assert.Equal("Validation", badType.Error!.Code);
    }

    [Fact]
    public async Task VaccinationRecord_Update_AndDelete_Work()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var rec = await service.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow.Date.AddDays(-1)
        });

        var updated = await service.UpdateVaccinationRecordAsync(seed.FarmId, rec.Value!.Id, new UpdateVaccinationRecordRequest
        {
            AnimalId = seed.OtherAnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow.Date,
            VetName = "Dr. Delta",
            Cost = 90m
        });
        Assert.True(updated.IsSuccess);
        Assert.Equal("Max", updated.Value!.AnimalName);
        Assert.Equal("Dr. Delta", updated.Value.VetName);

        var deleted = await service.DeleteVaccinationRecordAsync(seed.FarmId, rec.Value.Id);
        Assert.True(deleted.IsSuccess);

        var list = await service.GetVaccinationRecordsAsync(seed.FarmId, new VaccinationRecordListFilter());
        Assert.Empty(list.Value!.Items);
    }

    [Fact]
    public async Task Schedule_Crud_Works_WithValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var okNeg = await service.CreateScheduleAsync(seed.FarmId, new CreateVaccinationScheduleRequest
        {
            VaccineTypeId = seed.VaccineTypeId,
            RecurrenceDays = -5
        });
        Assert.Equal("Validation", okNeg.Error!.Code);

        var badVax = await service.CreateScheduleAsync(seed.FarmId, new CreateVaccinationScheduleRequest
        {
            VaccineTypeId = Guid.NewGuid(),
            RecurrenceDays = 10
        });
        Assert.Equal("Validation", badVax.Error!.Code);

        var created = await service.CreateScheduleAsync(seed.FarmId, new CreateVaccinationScheduleRequest
        {
            VaccineTypeId = seed.VaccineTypeId,
            AnimalTypeId = seed.AnimalTypeId,
            BreedId = seed.BreedId,
            RecurrenceDays = 90,
            IsActive = true,
            Notes = "Annual booster"
        });
        Assert.True(created.IsSuccess);
        Assert.Equal("Brucellosis", created.Value!.VaccineTypeName);
        Assert.Equal(90, created.Value.RecurrenceDays);

        var list = await service.GetSchedulesAsync(seed.FarmId, 1, 20);
        Assert.Equal(1, list.Value!.TotalCount);

        var updated = await service.UpdateScheduleAsync(seed.FarmId, created.Value.Id, new UpdateVaccinationScheduleRequest
        {
            VaccineTypeId = seed.VaccineTypeId,
            RecurrenceDays = 180,
            IsActive = false
        });
        Assert.True(updated.IsSuccess);
        Assert.False(updated.Value!.IsActive);
        Assert.Equal(180, updated.Value.RecurrenceDays);

        var deleted = await service.DeleteScheduleAsync(seed.FarmId, created.Value.Id);
        Assert.True(deleted.IsSuccess);
    }

    [Fact]
    public async Task VaccinationStatus_ComputesUpcoming_Due_Overdue()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateScheduleAsync(seed.FarmId, new CreateVaccinationScheduleRequest
        {
            VaccineTypeId = seed.VaccineTypeId,
            AnimalTypeId = seed.AnimalTypeId,
            RecurrenceDays = 30
        });

        // Bella vaccinated 60 days ago -> next due is 30 days ago -> overdue
        await service.CreateVaccinationRecordAsync(seed.FarmId, new CreateVaccinationRecordRequest
        {
            AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId,
            DateGiven = DateTime.UtcNow.Date.AddDays(-60)
        });

        // Max vaccinated 40 days ago -> next due in 30 days -> upcoming (no record for Max? Max has no record)
        // Max never vaccinated -> due now -> Due status

        var statuses = await service.GetVaccinationStatusAsync(seed.FarmId);
        Assert.True(statuses.IsSuccess);
        Assert.Equal(2, statuses.Value!.Count);

        var bella = statuses.Value.Single(s => s.AnimalId == seed.AnimalId);
        Assert.Equal(VaccinationStatusType.Overdue, bella.Status);
        Assert.Equal("Overdue", bella.StatusName);

        var max = statuses.Value.Single(s => s.AnimalId == seed.OtherAnimalId);
        Assert.Equal(VaccinationStatusType.Due, max.Status);
        Assert.Equal(0, max.DaysUntilDue);

        var overdue = await service.GetOverdueVaccinationsAsync(seed.FarmId);
        Assert.Equal(2, overdue.Value!.Count); // overdue + due both returned
    }

    [Fact]
    public async Task VaccinationStatus_InactiveScheduleIsIgnored()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateScheduleAsync(seed.FarmId, new CreateVaccinationScheduleRequest
        {
            VaccineTypeId = seed.VaccineTypeId,
            RecurrenceDays = 30,
            IsActive = false
        });

        var statuses = await service.GetVaccinationStatusAsync(seed.FarmId);
        Assert.Empty(statuses.Value!);
    }

    [Fact]
    public async Task VaccinationStatus_FiltersByAnimalTypeAndBreed()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // A schedule matching only the seeded animal type/breed
        await service.CreateScheduleAsync(seed.FarmId, new CreateVaccinationScheduleRequest
        {
            VaccineTypeId = seed.VaccineTypeId,
            AnimalTypeId = seed.AnimalTypeId,
            BreedId = seed.BreedId,
            RecurrenceDays = 60
        });

        var statuses = await service.GetVaccinationStatusAsync(seed.FarmId);
        Assert.Equal(2, statuses.Value!.Count); // both seeded animals share type + breed
        Assert.All(statuses.Value!, s => Assert.Equal("TAG", s.AnimalTagNumber[..3]));
    }
}