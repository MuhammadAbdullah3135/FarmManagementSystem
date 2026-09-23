using FMS.Application.Common;
using FMS.Application.Attendance;
using FMS.Infrastructure.Attendance;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class AttendanceTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static AttendanceService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(Guid FarmId, Guid AliId, Guid BilalId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var ali = new Employee
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
        var bilal = new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            FirstName = "Bilal",
            LastName = "Ahmed",
            SalaryType = SalaryType.Daily,
            SalaryRate = 1500,
            HireDate = DateTime.UtcNow.Date.AddMonths(-2),
            IsActive = true
        };

        context.Farms.Add(farm);
        context.Employees.AddRange(ali, bilal);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, ali.Id, bilal.Id);
    }

    [Fact]
    public async Task CheckIn_Succeeds_AndDuplicateReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var checkIn = await service.CheckInAsync(seed.FarmId, seed.AliId);
        Assert.True(checkIn.IsSuccess);
        Assert.Equal(AttendanceStatus.Present, checkIn.Value!.Status);
        Assert.NotNull(checkIn.Value.CheckInAt);
        Assert.Null(checkIn.Value.CheckOutAt);
        Assert.Equal(DateTime.UtcNow.Date, checkIn.Value.Date.Date);
        Assert.Equal("Ali Khan", checkIn.Value.EmployeeName);

        var duplicate = await service.CheckInAsync(seed.FarmId, seed.AliId);
        Assert.Equal("Conflict", duplicate.Error!.Code);
    }

    [Fact]
    public async Task CheckIn_UnknownEmployee_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CheckInAsync(seed.FarmId, Guid.NewGuid());
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task CheckOut_ComputesHours_AndDoubleCheckoutReturnsConflict()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        // No record yet
        var missing = await service.CheckOutAsync(seed.FarmId, seed.AliId);
        Assert.Equal("NotFound", missing.Error!.Code);

        await service.CheckInAsync(seed.FarmId, seed.AliId);
        var checkOut = await service.CheckOutAsync(seed.FarmId, seed.AliId);
        Assert.True(checkOut.IsSuccess);
        Assert.NotNull(checkOut.Value!.CheckOutAt);
        Assert.NotNull(checkOut.Value.HoursWorked); // same-day in/out → ~0 hours but present

        var again = await service.CheckOutAsync(seed.FarmId, seed.AliId);
        Assert.Equal("Conflict", again.Error!.Code);
    }

    // ── the queued rules: earliest check-in wins, latest check-out wins ──

    /// <summary>
    /// A shift starts when the employee was first at the gate, so a device reporting an earlier
    /// time than the stored one moves it back. This is the half of the rule 4.5.3 deliberately
    /// left unimplemented.
    /// </summary>
    [Fact]
    public async Task CheckIn_QueuedEarlierThanTheStoredOne_MovesTheStoredTimeBack()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var live = await service.CheckInAsync(seed.FarmId, seed.AliId);
        var storedTime = live.Value!.CheckInAt!.Value;

        var earlier = storedTime.AddHours(-2);
        var queued = await service.CheckInAsync(seed.FarmId, seed.AliId,
            new CheckInRequest { OccurredAt = earlier }, Guid.NewGuid());

        Assert.True(queued.IsSuccess);
        Assert.Equal(earlier, queued.Value!.CheckInAt);

        var stored = await context.AttendanceRecords.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == seed.AliId);
        Assert.Equal(earlier, stored.CheckInAt);
    }

    /// <summary>
    /// The later check-in is "already satisfied", not an error: the day does have a check-in,
    /// so a queue may clear the item — and the stored time is left where it is.
    /// </summary>
    [Fact]
    public async Task CheckIn_QueuedLaterThanTheStoredOne_IsSuperseded_AndChangesNothing()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var first = await service.CheckInAsync(seed.FarmId, seed.AliId,
            new CheckInRequest { OccurredAt = DateTime.UtcNow.Date.AddHours(6) });
        var storedTime = first.Value!.CheckInAt!.Value;

        var queued = await service.CheckInAsync(seed.FarmId, seed.AliId,
            new CheckInRequest { OccurredAt = storedTime.AddHours(1) }, Guid.NewGuid());

        Assert.Equal(Error.SupersededCode, queued.Error!.Code);
        Assert.Equal("Employee already has an attendance record for that day", queued.Error.Message);

        var stored = await context.AttendanceRecords.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == seed.AliId);
        Assert.Equal(storedTime, stored.CheckInAt);
    }

    /// <summary>
    /// A day somebody entered by hand has no check-in time for "earlier" to beat, and it says
    /// something a clock cannot overturn: it is reported as already satisfied, untouched, so a
    /// person decides rather than a device.
    /// </summary>
    [Fact]
    public async Task CheckIn_QueuedForADayEnteredByHand_IsSuperseded_AndTheManualRecordStands()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var date = DateTime.UtcNow.Date;
        await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.AliId,
            Date = date,
            Status = AttendanceStatus.Leave,
            Notes = "Approved leave"
        });

        var queued = await service.CheckInAsync(seed.FarmId, seed.AliId,
            new CheckInRequest { OccurredAt = date.AddHours(9) }, Guid.NewGuid());

        Assert.Equal(Error.SupersededCode, queued.Error!.Code);
        Assert.Contains("Leave", queued.Error.Message);

        var stored = await context.AttendanceRecords.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == seed.AliId);
        Assert.Equal(AttendanceStatus.Leave, stored.Status);
        Assert.Null(stored.CheckInAt);
    }

    [Fact]
    public async Task CheckOut_QueuedLaterThanTheStoredOne_MovesTheStoredTimeForward()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var day = DateTime.UtcNow.Date.AddDays(-1);
        await service.CheckInAsync(seed.FarmId, seed.AliId,
            new CheckInRequest { OccurredAt = day.AddHours(6) });
        await service.CheckOutAsync(seed.FarmId, seed.AliId,
            new CheckOutRequest { OccurredAt = day.AddHours(15) });

        var later = await service.CheckOutAsync(seed.FarmId, seed.AliId,
            new CheckOutRequest { OccurredAt = day.AddHours(18) }, Guid.NewGuid());

        Assert.True(later.IsSuccess);
        Assert.Equal(day.AddHours(18), later.Value!.CheckOutAt);

        var stored = await context.AttendanceRecords.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == seed.AliId);
        Assert.Equal(day.AddHours(18), stored.CheckOutAt);
        Assert.Equal(12d, stored.CheckOutAt!.Value.Subtract(stored.CheckInAt!.Value).TotalHours, 2);
    }

    [Fact]
    public async Task CheckOut_QueuedEarlierThanTheStoredOne_IsSuperseded_AndTheLaterTimeStands()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var day = DateTime.UtcNow.Date.AddDays(-1);
        await service.CheckInAsync(seed.FarmId, seed.AliId,
            new CheckInRequest { OccurredAt = day.AddHours(6) });
        await service.CheckOutAsync(seed.FarmId, seed.AliId,
            new CheckOutRequest { OccurredAt = day.AddHours(18) });

        var queued = await service.CheckOutAsync(seed.FarmId, seed.AliId,
            new CheckOutRequest { OccurredAt = day.AddHours(15) }, Guid.NewGuid());

        Assert.Equal(Error.SupersededCode, queued.Error!.Code);
        Assert.Equal("Employee has already checked out today", queued.Error.Message);

        var stored = await context.AttendanceRecords.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == seed.AliId);
        Assert.Equal(day.AddHours(18), stored.CheckOutAt);
    }

    [Fact]
    public async Task UpsertAttendance_CreatesThenUpdatesSameDate()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var date = DateTime.UtcNow.Date.AddDays(-1);
        var created = await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.BilalId,
            Date = date,
            Status = AttendanceStatus.Late,
            CheckInAt = date.AddHours(10),
            Notes = "Traffic delay"
        });
        Assert.True(created.IsSuccess);
        Assert.Equal(AttendanceStatus.Late, created.Value!.Status);
        Assert.Equal("Late", created.Value.StatusName);

        var updated = await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.BilalId,
            Date = date,
            Status = AttendanceStatus.Present,
            CheckInAt = date.AddHours(8),
            CheckOutAt = date.AddHours(17)
        });
        Assert.True(updated.IsSuccess);
        Assert.Equal(AttendanceStatus.Present, updated.Value!.Status);
        Assert.Equal(9m, updated.Value.HoursWorked);

        var list = await service.GetAttendanceAsync(seed.FarmId, new AttendanceListFilter { EmployeeId = seed.BilalId });
        Assert.Equal(1, list.Value!.TotalCount); // upsert did not duplicate
    }

    [Fact]
    public async Task UpsertAttendance_FutureDate_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.AliId,
            Date = DateTime.UtcNow.Date.AddDays(3)
        });
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task UpsertAttendance_CheckoutBeforeCheckin_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var date = DateTime.UtcNow.Date.AddDays(-1);
        var result = await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.AliId,
            Date = date,
            CheckInAt = date.AddHours(14),
            CheckOutAt = date.AddHours(9)
        });
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task GetAttendance_FiltersByStatusAndDateRange()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.AliId,
            Date = DateTime.UtcNow.Date.AddDays(-10),
            Status = AttendanceStatus.Absent
        });
        await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.AliId,
            Date = DateTime.UtcNow.Date.AddDays(-1),
            Status = AttendanceStatus.Present
        });

        var absentOnly = await service.GetAttendanceAsync(seed.FarmId, new AttendanceListFilter
        {
            Status = AttendanceStatus.Absent
        });
        Assert.Single(absentOnly.Value!.Items);

        var recent = await service.GetAttendanceAsync(seed.FarmId, new AttendanceListFilter
        {
            From = DateTime.UtcNow.Date.AddDays(-5),
            To = DateTime.UtcNow.Date
        });
        Assert.Equal(1, recent.Value!.TotalCount);
        Assert.Equal(AttendanceStatus.Present, recent.Value.Items[0].Status);
    }

    [Fact]
    public async Task DeleteAttendance_Succeeds_AndUnknownReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.UpsertAttendanceAsync(seed.FarmId, new UpsertAttendanceRequest
        {
            EmployeeId = seed.AliId,
            Date = DateTime.UtcNow.Date.AddDays(-1)
        });

        var deleted = await service.DeleteAttendanceAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleted.IsSuccess);

        var unknown = await service.DeleteAttendanceAsync(seed.FarmId, Guid.NewGuid());
        Assert.Equal("NotFound", unknown.Error!.Code);
    }
}
