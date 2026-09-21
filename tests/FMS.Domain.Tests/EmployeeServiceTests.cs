using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class EmployeeServiceTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static EmployeeService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(Guid FarmId, Guid DairyDeptId, Guid FieldDeptId, Guid MilkerRoleId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var dairy = new Department { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Dairy Dept" };
        var field = new Department { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Field Ops" };
        var milker = new EmployeeRole { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Milker", Description = "Milks animals" };

        context.Farms.Add(farm);
        context.Departments.AddRange(dairy, field);
        context.EmployeeRoles.Add(milker);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, dairy.Id, field.Id, milker.Id);
    }

    private static CreateEmployeeRequest AliRequest(SeedData seed, Action<CreateEmployeeRequest>? mutate = null)
    {
        var request = new CreateEmployeeRequest
        {
            FirstName = "Ali",
            LastName = "Khan",
            Phone = "+92 300 1234567",
            Email = "ali@farm.com",
            DepartmentId = seed.DairyDeptId,
            EmployeeRoleId = seed.MilkerRoleId,
            SalaryType = SalaryType.Monthly,
            SalaryRate = 50000,
            HireDate = DateTime.UtcNow.Date.AddMonths(-6)
        };
        mutate?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task CreateDepartment_Succeeds_AndDuplicateReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateDepartmentAsync(seed.FarmId, new CreateDepartmentRequest { Name = "Veterinary" });
        Assert.True(result.IsSuccess);
        Assert.Equal("Veterinary", result.Value!.Name);

        var duplicate = await service.CreateDepartmentAsync(seed.FarmId, new CreateDepartmentRequest { Name = "dairy dept" });
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Conflict", duplicate.Error!.Code);
    }

    [Fact]
    public async Task CreateRole_Succeeds_AndDuplicateReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateRoleAsync(seed.FarmId,
            new CreateEmployeeRoleRequest { Name = "Shepherd", Description = "Manages flock" });
        Assert.True(result.IsSuccess);
        Assert.Equal("Shepherd", result.Value!.Name);

        var duplicate = await service.CreateRoleAsync(seed.FarmId, new CreateEmployeeRoleRequest { Name = "MILKER" });
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Conflict", duplicate.Error!.Code);
    }

    [Fact]
    public async Task DeleteDepartment_BlockedWhenReferenced_OtherwiseSucceeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed));

        var blocked = await service.DeleteDepartmentAsync(seed.FarmId, seed.DairyDeptId);
        Assert.False(blocked.IsSuccess);
        Assert.Equal("Conflict", blocked.Error!.Code);

        var empty = await service.CreateDepartmentAsync(seed.FarmId, new CreateDepartmentRequest { Name = "Temp" });
        var deleted = await service.DeleteDepartmentAsync(seed.FarmId, empty.Value!.Id);
        Assert.True(deleted.IsSuccess);
    }

    [Fact]
    public async Task DeleteRole_ReferencedByEmployee_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed));

        var result = await service.DeleteRoleAsync(seed.FarmId, seed.MilkerRoleId);
        Assert.False(result.IsSuccess);
        Assert.Equal("Conflict", result.Error!.Code);
    }

    [Fact]
    public async Task CreateEmployee_Ali_Milker_DairyDept_Monthly_Succeeds()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed));

        Assert.True(result.IsSuccess);
        Assert.Equal("Ali", result.Value!.FirstName);
        Assert.Equal("Dairy Dept", result.Value.DepartmentName);
        Assert.Equal("Milker", result.Value.EmployeeRoleName);
        Assert.Equal("Monthly", result.Value.SalaryTypeName);
        Assert.Equal(50000m, result.Value.SalaryRate);
        Assert.True(result.Value.IsActive);
    }

    [Fact]
    public async Task CreateEmployee_Validations()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var negative = await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed, r => r.SalaryRate = -1));
        Assert.Equal("Validation", negative.Error!.Code);

        var badEmail = await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed, r => r.Email = "not-an-email"));
        Assert.Equal("Validation", badEmail.Error!.Code);

        var futureHire = await service.CreateEmployeeAsync(seed.FarmId,
            AliRequest(seed, r => r.HireDate = DateTime.UtcNow.Date.AddDays(10)));
        Assert.Equal("Validation", futureHire.Error!.Code);

        var unknownDept = await service.CreateEmployeeAsync(seed.FarmId,
            AliRequest(seed, r => r.DepartmentId = Guid.NewGuid()));
        Assert.Equal("NotFound", unknownDept.Error!.Code);
    }

    [Fact]
    public async Task UpdateEmployee_ChangesSalaryAndDepartment()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed));

        var result = await service.UpdateEmployeeAsync(seed.FarmId, created.Value!.Id, new UpdateEmployeeRequest
        {
            FirstName = "Ali",
            LastName = "Khan",
            DepartmentId = seed.FieldDeptId,
            EmployeeRoleId = seed.MilkerRoleId,
            SalaryType = SalaryType.Daily,
            SalaryRate = 1500,
            HireDate = created.Value.HireDate,
            IsActive = true
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("Field Ops", result.Value!.DepartmentName);
        Assert.Equal("Daily", result.Value.SalaryTypeName);
        Assert.Equal(1500m, result.Value.SalaryRate);
    }

    [Fact]
    public async Task DeleteEmployee_SoftDeletes_HiddenFromListAndDetail()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed));

        var delete = await service.DeleteEmployeeAsync(seed.FarmId, created.Value!.Id);
        Assert.True(delete.IsSuccess);

        var detail = await service.GetEmployeeByIdAsync(seed.FarmId, created.Value.Id);
        Assert.False(detail.IsSuccess);
        Assert.Equal("NotFound", detail.Error!.Code);

        var list = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter());
        Assert.Equal(0, list.Value!.TotalCount);

        Assert.True(await context.Employees.AnyAsync(e => e.Id == created.Value.Id && e.IsDeleted));
    }

    [Fact]
    public async Task GetEmployees_SearchFilters_Paged()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed));
        await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed, r =>
        {
            r.FirstName = "Bilal";
            r.LastName = "Ahmed";
            r.Email = "bilal@farm.com";
        }));
        await service.CreateEmployeeAsync(seed.FarmId, AliRequest(seed, r =>
        {
            r.FirstName = "Sara";
            r.LastName = "Ali";
            // Distinct from the first two on purpose: the email is the employee's
            // identifier and a duplicate is now refused, so two of these three sharing
            // the fixture's default address would be a conflict rather than a third row.
            r.Email = "sara@farm.com";
            r.DepartmentId = seed.FieldDeptId;
        }));

        var all = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter());
        Assert.Equal(3, all.Value!.TotalCount);

        var search = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter { Search = "ali" });
        Assert.Equal(2, search.Value!.TotalCount); // Ali Khan + Sara Ali

        var byDept = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter { DepartmentId = seed.FieldDeptId });
        Assert.Equal(1, byDept.Value!.TotalCount);
        Assert.Equal("Sara", byDept.Value.Items[0].FirstName);

        var paged = await service.GetEmployeesAsync(seed.FarmId, new EmployeeListFilter { Page = 2, PageSize = 2 });
        Assert.Equal(3, paged.Value!.TotalCount);
        Assert.Single(paged.Value.Items);
    }
}
