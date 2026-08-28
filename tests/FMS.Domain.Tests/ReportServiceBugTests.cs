using FMS.Application.Common;
using FMS.Application.Reports;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

/// <summary>
/// Report accuracy cross-checks: summary/report numbers must reconcile with raw records.
/// </summary>
public class ReportServiceBugTests
{
    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ReportService CreateService(FmsDbContext context) => new(context);

    // Employee report must exclude soft-deleted employees.

    [Fact]
    public async Task EmployeeReport_ShouldExcludeSoftDeleted_Employees()
    {
        using var context = CreateContext();
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var dept = new Department { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Herdsmen" };
        var role = new EmployeeRole { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Worker" };

        var alive = new Employee
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, FirstName = "John", LastName = "Doe",
            DepartmentId = dept.Id, SalaryType = SalaryType.Monthly, SalaryRate = 1000
        };
        var deleted = new Employee
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, FirstName = "Jane", LastName = "Doe",
            DepartmentId = dept.Id, SalaryType = SalaryType.Monthly, SalaryRate = 1000,
            IsDeleted = true, IsActive = false
        };

        context.Farms.Add(farm);
        context.Departments.Add(dept);
        context.EmployeeRoles.Add(role);
        context.Employees.AddRange(alive, deleted);
        await context.SaveChangesAsync();

        var svc = CreateService(context);
        var report = await svc.GetEmployeeReportAsync(farm.Id, null, null);
        Assert.True(report.IsSuccess);

        // EXPECTED: soft-deleted employee excluded -> TotalEmployees = 1.
        // ACTUAL (verified bug, ReportService.cs employee query lacks !IsDeleted): counts 2.
        Assert.Equal(1, report.Value!.TotalEmployees);
    }

    // Vaccination report overdue/upcoming must be computed, not always zero.

    [Fact]
    public async Task VaccinationReport_ShouldNotHardcode_OverdueUpcoming_ToZero()
    {
        using var context = CreateContext();
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var type = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var breed = new Breed { Id = Guid.NewGuid(), AnimalTypeId = type.Id, Name = "Holstein" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active" };
        var vaccine = new VaccineType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "FMD" };
        var animal = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = "T1", Name = "Bella",
            AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(type);
        context.Breeds.Add(breed);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.VaccineTypes.Add(vaccine);
        context.Animals.Add(animal);

        // An active schedule for this vaccine + animal type means due/upcoming status is representable.
        context.VaccinationSchedules.Add(new VaccinationSchedule
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, VaccineTypeId = vaccine.Id,
            AnimalTypeId = type.Id, RecurrenceDays = 30, IsActive = true
        });
        await context.SaveChangesAsync();

        var svc = CreateService(context);
        var report = await svc.GetVaccinationReportAsync(farm.Id, null, null);
        Assert.True(report.IsSuccess);

        // EXPECTED: with an active matching schedule, Overdue/Upcoming should be derived (non-zero here).
        // ACTUAL (verified bug, ReportService hardcodes OverdueCount=0/UpcomingCount=0): always zero.
        Assert.True(report.Value!.OverdueCount >= 1 || report.Value.UpcomingCount >= 1,
            $"Active schedule implies a non-zero Due/Overdue status. Got OverdueCount={report.Value.OverdueCount}, UpcomingCount={report.Value.UpcomingCount}.");
    }
}
