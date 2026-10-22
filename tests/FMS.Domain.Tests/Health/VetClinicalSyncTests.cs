using FMS.Application.Breeding;
using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Breeding;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

public class VetClinicalSyncTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
        public string? GetUserEmail() => null;
    }

    private sealed record Seed(
        Guid FarmId,
        Guid AnimalId,
        Guid OtherAnimalId,
        Guid VaccineTypeId,
        Guid SexOptionId,
        Guid BreedingRecordId);

    private static async Task<Seed> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Active",
            IsSystemDefined = true
        };
        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-1",
            AnimalTypeId = animalType.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        };
        var other = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-2",
            AnimalTypeId = animalType.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id
        };
        var vaccineType = new VaccineType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Vaccine"
        };
        var breeding = new BreedingRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            SireId = other.Id,
            DamId = animal.Id,
            BreedingDate = DateTime.UtcNow.AddMonths(-9),
            Method = BreedingMethod.Natural,
            CreatedAt = DateTime.UtcNow
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(animalType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.AddRange(animal, other);
        context.VaccineTypes.Add(vaccineType);
        context.BreedingRecords.Add(breeding);
        await context.SaveChangesAsync();

        return new Seed(farm.Id, animal.Id, other.Id, vaccineType.Id, sex.Id, breeding.Id);
    }

    private static Vet AddVet(FmsDbContext context, Guid farmId, string name)
    {
        var vet = new Vet
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = name,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        context.Vets.Add(vet);
        return vet;
    }

    [Fact]
    public async Task MedicalRecord_CreateAndUpdate_KeepVetIdAndVetNameInSync()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var vet = AddVet(context, seed.FarmId, "Dr. Selected");
        await context.SaveChangesAsync();
        var service = new MedicalRecordService(context, new FixedCurrentUser());

        var created = await service.CreateMedicalRecordAsync(seed.FarmId, new CreateMedicalRecordRequest
        {
            AnimalId = seed.AnimalId,
            Symptoms = "Fever",
            VetId = vet.Id,
            VetName = "ignored text",
            Cost = 0,
            DateRecorded = DateTime.UtcNow
        });
        Assert.True(created.IsSuccess);
        Assert.Equal(vet.Id, created.Value!.VetId);
        Assert.Equal("Dr. Selected", created.Value.VetName);

        var stored = await context.MedicalRecords.SingleAsync();
        Assert.Equal(vet.Id, stored.VetId);
        Assert.Equal("Dr. Selected", stored.VetName);

        var updated = await service.UpdateMedicalRecordAsync(seed.FarmId, stored.Id,
            new UpdateMedicalRecordRequest
            {
                AnimalId = seed.AnimalId,
                Symptoms = "Fever",
                VetId = null,
                VetName = "  Free text vet  ",
                Cost = 0,
                DateRecorded = DateTime.UtcNow,
                Status = MedicalRecordStatus.Open
            });
        Assert.True(updated.IsSuccess);
        Assert.Null(updated.Value!.VetId);
        Assert.Equal("Free text vet", updated.Value.VetName);

        var reread = await context.MedicalRecords.SingleAsync();
        Assert.Null(reread.VetId);
        Assert.Equal("Free text vet", reread.VetName);
    }

    [Fact]
    public async Task MedicalRecord_RejectsAVetFromAnotherFarm()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var foreignFarm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Other" };
        context.Farms.Add(foreignFarm);
        var foreignVet = AddVet(context, foreignFarm.Id, "Dr. Foreign");
        await context.SaveChangesAsync();
        var service = new MedicalRecordService(context, new FixedCurrentUser());

        var result = await service.CreateMedicalRecordAsync(seed.FarmId, new CreateMedicalRecordRequest
        {
            AnimalId = seed.AnimalId,
            Symptoms = "Fever",
            VetId = foreignVet.Id,
            Cost = 0,
            DateRecorded = DateTime.UtcNow
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Equal(DomainMessageKeys.VetNotFoundInFarm, result.Error.MessageKey);
        Assert.Empty(context.MedicalRecords);
    }

    [Fact]
    public async Task VaccinationRecord_CreateAndUpdate_KeepVetIdAndVetNameInSync()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var vet = AddVet(context, seed.FarmId, "Dr. Selected");
        await context.SaveChangesAsync();
        var service = new VaccineService(context, new FixedCurrentUser());

        var created = await service.CreateVaccinationRecordAsync(seed.FarmId,
            new CreateVaccinationRecordRequest
            {
                AnimalId = seed.AnimalId,
                VaccineTypeId = seed.VaccineTypeId,
                DateGiven = DateTime.UtcNow,
                VetId = vet.Id,
                VetName = "ignored text",
                Cost = 0
            });
        Assert.True(created.IsSuccess);
        Assert.Equal(vet.Id, created.Value!.VetId);
        Assert.Equal("Dr. Selected", created.Value.VetName);

        var stored = await context.VaccinationRecords.SingleAsync();
        var updated = await service.UpdateVaccinationRecordAsync(seed.FarmId, stored.Id,
            new UpdateVaccinationRecordRequest
            {
                AnimalId = seed.AnimalId,
                VaccineTypeId = seed.VaccineTypeId,
                DateGiven = DateTime.UtcNow,
                VetId = null,
                VetName = "  Free text vet  ",
                Cost = 0
            });
        Assert.True(updated.IsSuccess);
        Assert.Null(updated.Value!.VetId);
        Assert.Equal("Free text vet", updated.Value.VetName);

        var reread = await context.VaccinationRecords.SingleAsync();
        Assert.Null(reread.VetId);
        Assert.Equal("Free text vet", reread.VetName);
    }

    [Fact]
    public async Task BreedingRecord_CreateAndUpdate_KeepVetIdAndVetNameInSync()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var vet = AddVet(context, seed.FarmId, "Dr. Selected");
        await context.SaveChangesAsync();
        var service = new BreedingService(context, new FixedCurrentUser());

        var created = await service.CreateBreedingRecordAsync(seed.FarmId,
            new CreateBreedingRecordRequest
            {
                SireId = seed.OtherAnimalId,
                DamId = seed.AnimalId,
                BreedingDate = DateTime.UtcNow,
                Method = BreedingMethod.Natural,
                VetId = vet.Id,
                VetName = "ignored text"
            });
        Assert.True(created.IsSuccess);
        Assert.Equal(vet.Id, created.Value!.VetId);
        Assert.Equal("Dr. Selected", created.Value.VetName);

        var stored = await context.BreedingRecords
            .SingleAsync(record => record.Id != seed.BreedingRecordId);
        var updated = await service.UpdateBreedingRecordAsync(seed.FarmId, stored.Id,
            new UpdateBreedingRecordRequest
            {
                SireId = seed.OtherAnimalId,
                DamId = seed.AnimalId,
                BreedingDate = DateTime.UtcNow,
                Method = BreedingMethod.Natural,
                VetId = null,
                VetName = "  Free text vet  ",
                Result = BreedingResult.Pending
            });
        Assert.True(updated.IsSuccess);
        Assert.Null(updated.Value!.VetId);
        Assert.Equal("Free text vet", updated.Value.VetName);

        var reread = await context.BreedingRecords.SingleAsync(record => record.Id == stored.Id);
        Assert.Null(reread.VetId);
        Assert.Equal("Free text vet", reread.VetName);
    }

    [Fact]
    public async Task BirthRecord_Create_KeepsVetIdAndVetNameInSync()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var vet = AddVet(context, seed.FarmId, "Dr. Selected");
        await context.SaveChangesAsync();
        var service = new BreedingService(context, new FixedCurrentUser());

        var created = await service.CreateBirthRecordAsync(seed.FarmId,
            new CreateBirthRecordRequest
            {
                DamId = seed.AnimalId,
                BreedingRecordId = seed.BreedingRecordId,
                BirthDate = DateTime.UtcNow,
                VetId = vet.Id,
                VetName = "ignored text",
                Offspring =
                {
                    new CreateBirthOffspringRequest
                    {
                        SexOptionId = seed.SexOptionId,
                        Outcome = BirthOutcome.Alive,
                        Name = "Calf"
                    }
                }
            });
        Assert.True(created.IsSuccess);
        Assert.Equal(vet.Id, created.Value!.VetId);
        Assert.Equal("Dr. Selected", created.Value.VetName);

        var stored = await context.BirthRecords.SingleAsync();
        Assert.Equal(vet.Id, stored.VetId);
        Assert.Equal("Dr. Selected", stored.VetName);
    }
}
