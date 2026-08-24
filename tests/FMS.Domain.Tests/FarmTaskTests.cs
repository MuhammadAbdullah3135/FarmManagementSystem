using FMS.Application.Common;
using FMS.Application.Tasks;
using FMS.Domain.Common;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class FarmTaskTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static FarmTaskService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(Guid FarmId, Guid EmployeeId, Guid AnimalId, Guid LocationId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            FirstName = "Ali",
            LastName = "Khan",
            SalaryType = SalaryType.Monthly,
            SalaryRate = 50000,
            HireDate = DateTime.UtcNow.Date.AddYears(-1),
            IsActive = true
        };
        var locationType = new LocationType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn" };
        var location = new Location { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Barn 1", LocationTypeId = locationType.Id };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cow" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active };
        var animal = new Animal
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            TagNumber = "TAG-001",
            Name = "Bessie",
            AnimalTypeId = animalType.Id,
            SexOptionId = sex.Id,
            AnimalStatusId = status.Id,
            LocationId = location.Id
        };

        context.Farms.Add(farm);
        context.Employees.Add(employee);
        context.LocationTypes.Add(locationType);
        context.Locations.Add(location);
        context.AnimalTypes.Add(animalType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.Animals.Add(animal);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, employee.Id, animal.Id, location.Id);
    }

    private static CreateFarmTaskRequest ValidRequest(SeedData seed) => new()
    {
        Title = "Vaccinate cattle",
        Description = "Annual FMD vaccination round",
        Priority = FarmTaskPriority.High,
        DueDate = DateTime.UtcNow.Date.AddDays(3),
        AssignedEmployeeId = seed.EmployeeId,
        AnimalId = seed.AnimalId,
        LocationId = seed.LocationId
    };

    [Fact]
    public async Task CreateFarmTask_Succeeds_WithDefaultsAndReferences()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));

        Assert.True(result.IsSuccess);
        Assert.Equal("Vaccinate cattle", result.Value!.Title);
        Assert.Equal(FarmTaskStatus.Pending, result.Value.Status);
        Assert.Equal("Pending", result.Value.StatusName);
        Assert.Equal(FarmTaskPriority.High, result.Value.Priority);
        Assert.False(result.Value.IsOverdue);
        Assert.Equal("Ali Khan", result.Value.AssignedEmployeeName);
        Assert.Equal("Bessie", result.Value.AnimalName);
        Assert.Equal("Barn 1", result.Value.LocationName);
    }

    [Fact]
    public async Task CreateFarmTask_TitleRequired_AndTooLong_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var empty = await service.CreateFarmTaskAsync(seed.FarmId,
            new CreateFarmTaskRequest { Title = "  ", DueDate = DateTime.UtcNow.Date });
        Assert.Equal("Validation", empty.Error!.Code);

        var tooLong = await service.CreateFarmTaskAsync(seed.FarmId,
            new CreateFarmTaskRequest { Title = new string('x', 201), DueDate = DateTime.UtcNow.Date });
        Assert.Equal("Validation", tooLong.Error!.Code);
    }

    [Fact]
    public async Task CreateFarmTask_UnknownReferences_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var badEmployee = await service.CreateFarmTaskAsync(seed.FarmId,
            new CreateFarmTaskRequest { Title = "T", DueDate = DateTime.UtcNow.Date, AssignedEmployeeId = Guid.NewGuid() });
        Assert.Equal("NotFound", badEmployee.Error!.Code);

        var badAnimal = await service.CreateFarmTaskAsync(seed.FarmId,
            new CreateFarmTaskRequest { Title = "T", DueDate = DateTime.UtcNow.Date, AnimalId = Guid.NewGuid() });
        Assert.Equal("NotFound", badAnimal.Error!.Code);

        var badLocation = await service.CreateFarmTaskAsync(seed.FarmId,
            new CreateFarmTaskRequest { Title = "T", DueDate = DateTime.UtcNow.Date, LocationId = Guid.NewGuid() });
        Assert.Equal("NotFound", badLocation.Error!.Code);
    }

    [Fact]
    public async Task GetTasks_Filters_ByStatus_Priority_Employee_Search()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));
        await service.CreateFarmTaskAsync(seed.FarmId, new CreateFarmTaskRequest
        {
            Title = "Clean barn",
            Priority = FarmTaskPriority.Low,
            DueDate = DateTime.UtcNow.Date.AddDays(1)
        });

        var byStatus = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter
        {
            Status = FarmTaskStatus.Pending,
            Priority = FarmTaskPriority.High
        });
        var highPending = Assert.Single(byStatus.Value!.Items);
        Assert.Equal("Vaccinate cattle", highPending.Title);

        var byEmployee = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter
        {
            AssignedEmployeeId = seed.EmployeeId
        });
        Assert.Single(byEmployee.Value!.Items);

        var bySearch = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter { Search = "barn" });
        Assert.Single(bySearch.Value!.Items); // title match only ("Clean barn")

        var searchTitleOnly = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter { Search = "Vaccinate" });
        Assert.Single(searchTitleOnly.Value!.Items);
    }

    [Fact]
    public async Task GetTasks_OverdueOnly_ReturnsOpenOverdueTasks()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateFarmTaskAsync(seed.FarmId, new CreateFarmTaskRequest
        {
            Title = "Overdue open",
            DueDate = DateTime.UtcNow.Date.AddDays(-2)
        });
        await service.CreateFarmTaskAsync(seed.FarmId, new CreateFarmTaskRequest
        {
            Title = "Future",
            DueDate = DateTime.UtcNow.Date.AddDays(5)
        });

        var overdue = await service.GetTasksAsync(seed.FarmId, new FarmTaskListFilter { IncludeOverdueOnly = true });
        var task = Assert.Single(overdue.Value!.Items);
        Assert.Equal("Overdue open", task.Title);
        Assert.True(task.IsOverdue);
    }

    [Fact]
    public async Task UpdateTask_Succeeds_ButClosedTaskReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));

        var updated = await service.UpdateFarmTaskAsync(seed.FarmId, created.Value!.Id, new UpdateFarmTaskRequest
        {
            Title = "Vaccinate all cattle",
            Description = null,
            Priority = FarmTaskPriority.Medium,
            DueDate = DateTime.UtcNow.Date.AddDays(10),
            AssignedEmployeeId = seed.EmployeeId,
            AnimalId = seed.AnimalId,
            LocationId = seed.LocationId
        });
        Assert.True(updated.IsSuccess);
        Assert.Equal("Vaccinate all cattle", updated.Value!.Title);
        Assert.Equal(FarmTaskPriority.Medium, updated.Value.Priority);

        await service.CompleteTaskAsync(seed.FarmId, created.Value.Id, new CompleteFarmTaskRequest());
        var editClosed = await service.UpdateFarmTaskAsync(seed.FarmId, created.Value.Id, new UpdateFarmTaskRequest
        {
            Title = "Should fail",
            Priority = FarmTaskPriority.Medium,
            DueDate = DateTime.UtcNow.Date
        });
        Assert.Equal("Conflict", editClosed.Error!.Code);
    }

    [Fact]
    public async Task DeleteTask_Succeeds_AndUnknownReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));

        var deleted = await service.DeleteFarmTaskAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleted.IsSuccess);

        var gone = await service.GetTaskByIdAsync(seed.FarmId, created.Value.Id);
        Assert.Equal("NotFound", gone.Error!.Code);

        var unknown = await service.DeleteFarmTaskAsync(seed.FarmId, Guid.NewGuid());
        Assert.Equal("NotFound", unknown.Error!.Code);
    }

    [Fact]
    public async Task StartTask_SetsInProgress_AndNonPendingReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));

        var started = await service.StartTaskAsync(seed.FarmId, created.Value!.Id);
        Assert.True(started.IsSuccess);
        Assert.Equal(FarmTaskStatus.InProgress, started.Value!.Status);
        Assert.NotNull(started.Value.StartedAt);

        var again = await service.StartTaskAsync(seed.FarmId, created.Value.Id);
        Assert.Equal("Conflict", again.Error!.Code);
    }

    [Fact]
    public async Task CompleteTask_FromPendingOrInProgress_Succeeds_AndStoresNotes()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var pending = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));
        var completedDirect = await service.CompleteTaskAsync(seed.FarmId, pending.Value!.Id,
            new CompleteFarmTaskRequest { CompletionNotes = "Done in one go" });
        Assert.True(completedDirect.IsSuccess);
        Assert.Equal(FarmTaskStatus.Completed, completedDirect.Value!.Status);
        Assert.NotNull(completedDirect.Value.CompletedAt);
        Assert.Equal("Done in one go", completedDirect.Value.CompletionNotes);

        var second = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));
        await service.StartTaskAsync(seed.FarmId, second.Value!.Id);
        var completedAfterStart = await service.CompleteTaskAsync(seed.FarmId, second.Value.Id,
            new CompleteFarmTaskRequest());
        Assert.Equal(FarmTaskStatus.Completed, completedAfterStart.Value!.Status);
    }

    [Fact]
    public async Task CompleteTask_AlreadyCompleted_ReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));
        await service.CompleteTaskAsync(seed.FarmId, created.Value!.Id, new CompleteFarmTaskRequest());

        var again = await service.CompleteTaskAsync(seed.FarmId, created.Value.Id, new CompleteFarmTaskRequest());
        Assert.Equal("Conflict", again.Error!.Code);
    }

    [Fact]
    public async Task CompleteTask_AnimalLinked_WritesTimelineEvent()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));
        await service.CompleteTaskAsync(seed.FarmId, created.Value!.Id,
            new CompleteFarmTaskRequest { CompletionNotes = "Vaccine administered" });

        var events = await context.AnimalTimelineEvents
            .Where(e => e.AnimalId == seed.AnimalId && e.EventType == TimelineEventTypes.TaskCompleted)
            .ToListAsync();
        var timelineEvent = Assert.Single(events);
        Assert.Contains("Vaccinate cattle", timelineEvent.Title);
        Assert.Equal(created.Value.Id, timelineEvent.RelatedEntityId);
    }

    [Fact]
    public async Task CancelTask_Succeeds_StoresReason_AndAlreadyCancelledReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));
        var cancelled = await service.CancelTaskAsync(seed.FarmId, created.Value!.Id,
            new CancelFarmTaskRequest { Reason = "No longer needed" });

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(FarmTaskStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal("No longer needed", cancelled.Value.CancelReason);

        var again = await service.CancelTaskAsync(seed.FarmId, created.Value.Id, new CancelFarmTaskRequest());
        Assert.Equal("Conflict", again.Error!.Code);
    }

    [Fact]
    public async Task ReopenTask_ClearsCompletionData_RestoresPending()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateFarmTaskAsync(seed.FarmId, ValidRequest(seed));
        await service.CompleteTaskAsync(seed.FarmId, created.Value!.Id,
            new CompleteFarmTaskRequest { CompletionNotes = "Wrongly closed" });

        var reopened = await service.ReopenTaskAsync(seed.FarmId, created.Value.Id);
        Assert.True(reopened.IsSuccess);
        Assert.Equal(FarmTaskStatus.Pending, reopened.Value!.Status);
        Assert.Null(reopened.Value.CompletedAt);
        Assert.Null(reopened.Value.CompletionNotes);
        Assert.Null(reopened.Value.StartedAt);

        // And it can be completed again after reopen
        var recompleted = await service.CompleteTaskAsync(seed.FarmId, created.Value.Id,
            new CompleteFarmTaskRequest { CompletionNotes = "Actually done now" });
        Assert.True(recompleted.IsSuccess);
    }
}
