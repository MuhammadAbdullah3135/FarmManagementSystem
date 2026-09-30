using System.Security.Claims;
using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class SalaryPaymentTests
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
        public string? GetUserEmail() => null;
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
            SalaryType = SalaryType.Weekly,
            SalaryRate = 5200,
            HireDate = DateTime.UtcNow.Date.AddMonths(-3),
            IsActive = true
        };

        context.Farms.Add(farm);
        context.Employees.AddRange(ali, bilal);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, ali.Id, bilal.Id);
    }

    [Fact]
    public async Task RecordSalaryPayment_Succeeds_WithDefaultsAndSnapshot()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 50000 });

        Assert.True(result.IsSuccess);
        Assert.Equal(50000, result.Value!.Amount);
        Assert.Equal(seed.AliId, result.Value.EmployeeId);
        Assert.Equal("Ali Khan", result.Value.EmployeeName);
        Assert.Equal(SalaryType.Monthly, result.Value.SalaryType);
        Assert.Equal(DateTime.UtcNow.Date, result.Value.PaymentDate.Date);
    }

    [Fact]
    public async Task RecordSalaryPayment_EmployeeNotFound_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.RecordSalaryPaymentAsync(seed.FarmId, Guid.NewGuid(),
            new RecordSalaryPaymentRequest { Amount = 100 });

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task RecordSalaryPayment_NonPositiveAmount_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var zero = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 0 });
        Assert.Equal("Validation", zero.Error!.Code);

        var negative = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = -500 });
        Assert.Equal("Validation", negative.Error!.Code);
    }

    [Fact]
    public async Task RecordSalaryPayment_FutureDate_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 100, PaymentDate = DateTime.UtcNow.Date.AddDays(5) });

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }

    [Fact]
    public async Task GetSalaryPayments_Paged_OrderedDescending()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        for (var i = 5; i >= 1; i--)
        {
            await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
                new RecordSalaryPaymentRequest
                {
                    Amount = 100 * i,
                    PaymentDate = DateTime.UtcNow.Date.AddDays(-i)
                });
        }

        var page1 = await service.GetSalaryPaymentsAsync(seed.FarmId, seed.AliId, 1, 3);
        Assert.True(page1.IsSuccess);
        Assert.Equal(5, page1.Value!.TotalCount);
        Assert.Equal(3, page1.Value.Items.Count);
        Assert.Equal(100, page1.Value.Items[0].Amount); // newest first (-1 day)
        Assert.Equal(300, page1.Value.Items[2].Amount);

        var page2 = await service.GetSalaryPaymentsAsync(seed.FarmId, seed.AliId, 2, 3);
        Assert.Equal(2, page2.Value!.Items.Count);
        Assert.Equal(500, page2.Value.Items[1].Amount); // oldest last (-5 days)
    }

    [Fact]
    public async Task GetSalaryPayments_EmployeeNotFound_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.GetSalaryPaymentsAsync(seed.FarmId, Guid.NewGuid(), 1, 20);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteSalaryPayment_Succeeds_AndRemovesFromList()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var payment = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 25000 });

        var deleted = await service.DeleteSalaryPaymentAsync(seed.FarmId, seed.AliId, payment.Value!.Id, "Entered twice by mistake");
        Assert.True(deleted.IsSuccess);

        var list = await service.GetSalaryPaymentsAsync(seed.FarmId, seed.AliId, 1, 20);
        Assert.Empty(list.Value!.Items);
    }

    [Fact]
    public async Task DeleteSalaryPayment_WrongEmployeeOrMissing_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var payment = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 25000 });

        // Payment exists but belongs to a different employee
        var wrongEmployee = await service.DeleteSalaryPaymentAsync(seed.FarmId, seed.BilalId, payment.Value!.Id, "Correction");
        Assert.Equal("NotFound", wrongEmployee.Error!.Code);

        // Unknown payment id
        var unknown = await service.DeleteSalaryPaymentAsync(seed.FarmId, seed.AliId, Guid.NewGuid(), "Correction");
        Assert.Equal("NotFound", unknown.Error!.Code);
    }

    // ── Ledger fields, recorded-by, and delete reasons ─────────────────────────

    private sealed class FixedCurrentUserWithEmail : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
        public string? GetUserEmail() => "manager@farm.test";
    }

    [Fact]
    public async Task RecordSalaryPayment_StoresLedgerFields_AndTrimsBlankReference()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var period = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest
            {
                Amount = 50000,
                PaymentType = SalaryPaymentType.BankTransfer,
                Reference = "  TXN-991  ",
                PeriodCovered = period
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(SalaryPaymentType.BankTransfer, result.Value!.PaymentType);
        Assert.Equal("BankTransfer", result.Value.PaymentTypeName);
        Assert.Equal("TXN-991", result.Value.Reference);
        Assert.Equal(period, result.Value.PeriodCovered);

        var blank = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 10, Reference = "   " });
        Assert.Null(blank.Value!.Reference);
        Assert.Equal(SalaryPaymentType.Unspecified, blank.Value!.PaymentType);
    }

    [Fact]
    public async Task RecordSalaryPayment_FuturePeriodOrOverlongReference_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var futurePeriod = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 100, PeriodCovered = DateTime.UtcNow.AddDays(7) });
        Assert.Equal("Validation", futurePeriod.Error!.Code);

        var longReference = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 100, Reference = new string('x', 101) });
        Assert.Equal("Validation", longReference.Error!.Code);
    }

    [Fact]
    public async Task RecordSalaryPayment_DeletedEmployee_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var employee = await context.Employees.SingleAsync(e => e.Id == seed.AliId);
        employee.IsDeleted = true;
        await context.SaveChangesAsync();

        var result = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 100 });

        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task RecordSalaryPayment_SnapshotsRecordedByIdentity()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = new EmployeeService(context, new FixedCurrentUserWithEmail());

        var result = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 100 });

        Assert.True(result.IsSuccess);
        Assert.Equal("manager@farm.test", result.Value!.RecordedByEmail);
        Assert.Equal("manager@farm.test", result.Value.RecordedByName); // no user row, so the email claim stands in
        Assert.True(result.Value.RecordedAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task GetSalaryPayments_ResolvesRecordedBy_FromUserAccounts()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var payerId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = payerId,
            AccountId = Guid.NewGuid(),
            Email = "bursar@farm.test",
            FirstName = "Fatima",
            LastName = "Noor",
            PasswordHash = "x",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var payment = new SalaryPayment
        {
            Id = Guid.NewGuid(), FarmId = seed.FarmId, EmployeeId = seed.AliId,
            Amount = 500, PaymentDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            CreatedBy = payerId
        };
        context.SalaryPayments.Add(payment);
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var result = await service.GetSalaryPaymentsAsync(seed.FarmId, seed.AliId, 1, 20);

        var row = result.Value!.Items.Single(p => p.Id == payment.Id);
        Assert.Equal("Fatima Noor", row.RecordedByName);
        Assert.Equal("bursar@farm.test", row.RecordedByEmail);
    }

    [Fact]
    public async Task DeleteSalaryPayment_ReasonRequired_AndWrittenToAuditLog()
    {
        var userId = Guid.NewGuid();
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "TestAuth"))
            }
        };
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditLogInterceptor(httpContextAccessor))
            .Options;
        using var context = new FmsDbContext(options);
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var missing = await service.DeleteSalaryPaymentAsync(seed.FarmId, seed.AliId, Guid.NewGuid(), "   ");
        Assert.Equal("Validation", missing.Error!.Code);

        var payment = await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 25000 });

        var deleted = await service.DeleteSalaryPaymentAsync(seed.FarmId, seed.AliId, payment.Value!.Id, "Wrong employee");
        Assert.True(deleted.IsSuccess);

        var entry = await context.AuditLogs
            .SingleAsync(l => l.EntityType == "SalaryPayment" && l.Action == AuditAction.Delete);
        Assert.Contains("Wrong employee", entry.OldValues);
        Assert.Contains("25000", entry.OldValues);
    }

    [Fact]
    public async Task PayrollReport_SumsPayments_ByEmployee_AndExpectedRunRate()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 50000, PaymentDate = DateTime.UtcNow.Date.AddDays(-10) });
        await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 50000, PaymentDate = DateTime.UtcNow.Date.AddDays(-40) }); // outside default range? no — default is 30 days back from today, -40 excluded only when defaults used; here we pass explicit range below
        await service.RecordSalaryPaymentAsync(seed.FarmId, seed.BilalId,
            new RecordSalaryPaymentRequest { Amount = 5200, PaymentDate = DateTime.UtcNow.Date.AddDays(-2) });

        var report = await service.GetPayrollReportAsync(seed.FarmId,
            DateTime.UtcNow.Date.AddDays(-60), DateTime.UtcNow.Date);

        Assert.True(report.IsSuccess);
        Assert.Equal(105200, report.Value!.TotalPaid);
        Assert.Equal(3, report.Value.PaymentCount);

        // Run-rate: Ali monthly 50,000 + Bilal weekly 5,200 × 52/12 ≈ 72,533.33
        Assert.Equal(Math.Round(50000 + 5200m * 52m / 12m, 2), report.Value.ExpectedMonthlyPayroll);

        Assert.Equal(2, report.Value.ByEmployee.Count);
        var aliRow = report.Value.ByEmployee.Single(x => x.EmployeeName == "Ali Khan");
        Assert.Equal(100000, aliRow.TotalPaid);
        Assert.Equal(2, aliRow.PaymentCount);
        var bilalRow = report.Value.ByEmployee.Single(x => x.EmployeeName == "Bilal Ahmed");
        Assert.Equal(5200, bilalRow.TotalPaid);
        Assert.Equal(1, bilalRow.PaymentCount);
    }

    [Fact]
    public async Task PayrollReport_DateRangeFilter_ExcludesOutsideRange()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 100, PaymentDate = DateTime.UtcNow.Date.AddDays(-90) });
        await service.RecordSalaryPaymentAsync(seed.FarmId, seed.AliId,
            new RecordSalaryPaymentRequest { Amount = 200, PaymentDate = DateTime.UtcNow.Date.AddDays(-5) });

        var report = await service.GetPayrollReportAsync(seed.FarmId,
            DateTime.UtcNow.Date.AddDays(-30), DateTime.UtcNow.Date);

        Assert.True(report.IsSuccess);
        Assert.Equal(200, report.Value!.TotalPaid);
        Assert.Equal(1, report.Value.PaymentCount);
    }

    [Fact]
    public async Task PayrollReport_FromAfterTo_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.GetPayrollReportAsync(seed.FarmId,
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(-30));

        Assert.False(result.IsSuccess);
        Assert.Equal("Validation", result.Error!.Code);
    }
}
