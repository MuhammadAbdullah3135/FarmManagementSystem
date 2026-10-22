using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Health;

public class VetTests
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

    private static VetService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private static Farm CreateFarm(FmsDbContext context, string name)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = name };
        context.Farms.Add(farm);
        return farm;
    }

    [Fact]
    public async Task Vets_ArePerFarm_AndNamesAreUniqueWithoutRegardToCase()
    {
        using var context = CreateContext();
        var farmA = CreateFarm(context, "Farm A");
        var farmB = CreateFarm(context, "Farm B");
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var first = await service.CreateVetAsync(
            farmA.Id, new CreateVetRequest { Name = "  Dr. Smith  " });
        Assert.True(first.IsSuccess);
        Assert.Equal("Dr. Smith", first.Value!.Name);

        var duplicate = await service.CreateVetAsync(
            farmA.Id, new CreateVetRequest { Name = "  dr. SMITH  " });
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Conflict", duplicate.Error!.Code);
        Assert.Equal(DomainMessageKeys.VetNameExists, duplicate.Error.MessageKey);
        Assert.Equal(1, await context.Vets.CountAsync(v => v.FarmId == farmA.Id));

        // The same name is not a duplicate on a different farm: vets do not cross farms.
        var otherFarm = await service.CreateVetAsync(
            farmB.Id, new CreateVetRequest { Name = "DR. SMITH" });
        Assert.True(otherFarm.IsSuccess);

        var list = await service.GetVetsAsync(farmA.Id, includeInactive: true);
        Assert.True(list.IsSuccess);
        var only = Assert.Single(list.Value!);
        Assert.Equal("Dr. Smith", only.Name);
    }

    [Fact]
    public async Task LicenceNumber_IsTrimmed_StoredAsNullWhenBlank_AndUniqueWhenPresent()
    {
        using var context = CreateContext();
        var farm = CreateFarm(context, "Farm");
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var licensed = await service.CreateVetAsync(farm.Id, new CreateVetRequest
        {
            Name = "Dr. Licence",
            LicenseNumber = "  LIC-7  "
        });
        Assert.True(licensed.IsSuccess);
        Assert.Equal("LIC-7", licensed.Value!.LicenseNumber);

        var duplicate = await service.CreateVetAsync(farm.Id, new CreateVetRequest
        {
            Name = "Dr. Copy",
            LicenseNumber = " lic-7 "
        });
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Conflict", duplicate.Error!.Code);
        Assert.Equal(DomainMessageKeys.VetLicenseNumberExists, duplicate.Error.MessageKey);

        var blank = await service.CreateVetAsync(farm.Id, new CreateVetRequest
        {
            Name = "Dr. Blank",
            LicenseNumber = "   "
        });
        Assert.True(blank.IsSuccess);
        Assert.Null(blank.Value!.LicenseNumber);
    }

    [Fact]
    public async Task Rename_DoesNotRewriteHistoricalVetName_ButDeleteIsBlockedByReferences()
    {
        using var context = CreateContext();
        var farm = CreateFarm(context, "Farm");
        var vet = new Vet
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Dr. Old",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-1"
        };
        context.Vets.Add(vet);
        context.Animals.Add(animal);
        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            Symptoms = "Lameness",
            VetId = vet.Id,
            VetName = "Dr. Old",
            DateRecorded = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var update = await service.UpdateVetAsync(farm.Id, vet.Id, new UpdateVetRequest
        {
            Name = "Dr. Renamed",
            IsActive = true
        });
        Assert.True(update.IsSuccess);

        // The retained clinical string is history; the read prefers the joined current name.
        var stored = await context.MedicalRecords.SingleAsync();
        Assert.Equal("Dr. Old", stored.VetName);
        Assert.Equal(vet.Id, stored.VetId);

        var read = await new MedicalRecordService(context, new FixedCurrentUser())
            .GetMedicalRecordByIdAsync(farm.Id, stored.Id);
        Assert.True(read.IsSuccess);
        Assert.Equal("Dr. Renamed", read.Value!.VetName);
        Assert.Equal(vet.Id, read.Value.VetId);

        var delete = await service.DeleteVetAsync(farm.Id, vet.Id);
        Assert.False(delete.IsSuccess);
        Assert.Equal("Conflict", delete.Error!.Code);
        Assert.Equal(DomainMessageKeys.VetHasClinicalRecords, delete.Error.MessageKey);
        Assert.Equal(1, await context.Vets.CountAsync());
    }

    [Fact]
    public async Task CostReport_StillGroupsByTheRetainedVetName()
    {
        using var context = CreateContext();
        var farm = CreateFarm(context, "Farm");
        var vet = new Vet
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Dr. Current",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var animal = new Animal { Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "TAG-1" };
        var vaccineType = new VaccineType
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            Name = "Vaccine"
        };
        context.Vets.Add(vet);
        context.Animals.Add(animal);
        context.VaccineTypes.Add(vaccineType);
        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            Symptoms = "Check",
            VetId = vet.Id,
            VetName = "Dr. Historical",
            Cost = 100m,
            DateRecorded = DateTime.UtcNow
        });
        context.VaccinationRecords.Add(new VaccinationRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            VaccineTypeId = vaccineType.Id,
            DateGiven = DateTime.UtcNow,
            VetId = vet.Id,
            VetName = "Dr. Historical",
            Cost = 50m
        });
        await context.SaveChangesAsync();

        var result = await new HealthCostService(context)
            .GetCostsByVetAsync(farm.Id, new HealthCostFilter());

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!);
        Assert.Equal("Dr. Historical", row.VetName);
        Assert.Equal(150m, row.TotalCost);
        Assert.Equal(2, row.RecordCount);
    }

    [Fact]
    public async Task Backfill_IsDryByDefault_IsIdempotent_AndLinksAllFourClinicalTables()
    {
        using var context = CreateContext();
        var farm = CreateFarm(context, "Farm");
        var animal = new Animal { Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "TAG-1" };
        var vaccineType = new VaccineType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Vaccine" };
        context.Animals.Add(animal);
        context.VaccineTypes.Add(vaccineType);

        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            Symptoms = "Check",
            VetName = "Dr. Alpha",
            DateRecorded = DateTime.UtcNow
        });
        context.VaccinationRecords.Add(new VaccinationRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            AnimalId = animal.Id,
            VaccineTypeId = vaccineType.Id,
            DateGiven = DateTime.UtcNow,
            VetName = "dr. alpha"
        });
        context.BreedingRecords.Add(new BreedingRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            SireId = animal.Id,
            DamId = Guid.NewGuid(),
            BreedingDate = DateTime.UtcNow,
            VetName = "  Dr. Beta  "
        });
        context.BirthRecords.Add(new BirthRecord
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            DamId = animal.Id,
            BirthDate = DateTime.UtcNow,
            VetName = "   "
        });
        await context.SaveChangesAsync();

        var service = new VetBackfillService(context);

        var dryRun = await service.RunAsync(apply: false);
        Assert.False(dryRun.Applied);
        Assert.Equal(2, dryRun.VetsCreated);
        Assert.Equal(1, dryRun.MedicalRecordsLinked);
        Assert.Equal(1, dryRun.VaccinationRecordsLinked);
        Assert.Equal(1, dryRun.BreedingRecordsLinked);
        Assert.Equal(0, dryRun.BirthRecordsLinked);
        Assert.Equal(1, dryRun.BlankVetNamesLeftNull);
        Assert.Equal(0, await context.Vets.CountAsync());
        Assert.All(await context.MedicalRecords.ToListAsync(), record => Assert.Null(record.VetId));

        var applied = await service.RunAsync(apply: true);
        Assert.True(applied.Applied);
        Assert.Equal(2, applied.VetsCreated);
        Assert.Equal(1, applied.MedicalRecordsLinked);
        Assert.Equal(1, applied.VaccinationRecordsLinked);
        Assert.Equal(1, applied.BreedingRecordsLinked);
        Assert.Equal(0, applied.BirthRecordsLinked);
        Assert.Equal(1, applied.BlankVetNamesLeftNull);

        var vets = await context.Vets.OrderBy(v => v.Name).ToListAsync();
        Assert.Equal(2, vets.Count);
        var alpha = Assert.Single(vets, v => v.Name.Equals("Dr. Alpha", StringComparison.OrdinalIgnoreCase));
        var beta = Assert.Single(vets, v => v.Name == "Dr. Beta");

        var medical = await context.MedicalRecords.SingleAsync();
        var vaccination = await context.VaccinationRecords.SingleAsync();
        var breeding = await context.BreedingRecords.SingleAsync();
        Assert.Equal(alpha.Id, medical.VetId);
        Assert.Equal(alpha.Id, vaccination.VetId);
        Assert.Equal(beta.Id, breeding.VetId);

        // The backfill never rewrites the retained string.
        Assert.Equal("Dr. Alpha", medical.VetName);
        Assert.Equal("dr. alpha", vaccination.VetName);
        Assert.Equal("  Dr. Beta  ", breeding.VetName);

        var secondRun = await service.RunAsync(apply: true);
        Assert.Equal(0, secondRun.VetsCreated);
        Assert.Equal(0, secondRun.MedicalRecordsLinked);
        Assert.Equal(0, secondRun.VaccinationRecordsLinked);
        Assert.Equal(0, secondRun.BreedingRecordsLinked);
        Assert.Equal(0, secondRun.BirthRecordsLinked);
        Assert.Equal(1, secondRun.BlankVetNamesLeftNull);
        Assert.Equal(2, await context.Vets.CountAsync());
    }
}
