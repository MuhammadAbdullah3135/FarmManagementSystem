using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Application.Employees;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FMS.Domain.Tests.Demo;

/// <summary>
/// The demo account's restricted actions. Two things are asserted here and nowhere else:
/// <list type="number">
/// <item><description>the identity check reads the real <c>User.IsDemo</c> column rather than
/// trusting anything a caller passes in, and</description></item>
/// <item><description>each guard returns a localisable refusal (a <c>validation.demo.*</c> key),
/// and each wired call site actually consults it — refusing the demo user while leaving a real
/// user unaffected.</description></item>
/// </list>
/// </summary>
public class DemoGuardsTests
{
    private readonly FmsDbContext _context;
    private readonly Guid _demoUserId = Guid.NewGuid();
    private readonly Guid _realUserId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();

    private const string DemoEmail = "demo@farm.local";
    private const string RealEmail = "owner@example.com";

    public DemoGuardsTests()
    {
        _context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _context.Users.AddRange(
            new User
            {
                Id = _demoUserId,
                AccountId = _accountId,
                Email = DemoEmail,
                PasswordHash = "not-a-real-credential",
                FirstName = "Demo",
                LastName = "User",
                IsActive = true,
                IsDemo = true
            },
            new User
            {
                Id = _realUserId,
                AccountId = _accountId,
                Email = RealEmail,
                PasswordHash = "not-a-real-credential",
                FirstName = "Real",
                LastName = "Owner",
                IsActive = true,
                IsDemo = false
            });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    // ── Identity: IsDemoUserAsync / CurrentUserIsDemoAsync ──────────────────────────────────

    [Fact]
    public async Task IsDemoUserAsync_is_true_only_for_the_user_with_the_demo_flag()
    {
        Assert.True(await DemoGuards.IsDemoUserAsync(_context, _demoUserId));
        Assert.False(await DemoGuards.IsDemoUserAsync(_context, _realUserId));
        Assert.False(await DemoGuards.IsDemoUserAsync(_context, Guid.NewGuid()));
    }

    [Fact]
    public async Task IsDemoUserAsync_follows_the_stored_flag_and_ignores_any_passed_in_value()
    {
        // The check must read the database, not echo an argument. Flipping the stored flag
        // must flip the answer.
        var real = await _context.Users.FindAsync(_realUserId);
        real!.IsDemo = true;
        await _context.SaveChangesAsync();

        Assert.True(await DemoGuards.IsDemoUserAsync(_context, _realUserId));

        var demo = await _context.Users.FindAsync(_demoUserId);
        demo!.IsDemo = false;
        await _context.SaveChangesAsync();

        Assert.False(await DemoGuards.IsDemoUserAsync(_context, _demoUserId));
    }

    [Fact]
    public async Task CurrentUserIsDemoAsync_reports_the_same_as_the_real_check()
    {
        Assert.True(await DemoGuards.CurrentUserIsDemoAsync(_context, _demoUserId));
        Assert.False(await DemoGuards.CurrentUserIsDemoAsync(_context, _realUserId));
    }

    // ── Guard shape: every guard is a localisable validation failure ────────────────────────

    [Theory]
    [InlineData("password-reset", DomainMessageKeys.DemoPasswordResetDisabled)]
    [InlineData("password-change", DomainMessageKeys.DemoPasswordChangeDisabled)]
    [InlineData("email", DomainMessageKeys.DemoEmailSendingDisabled)]
    [InlineData("payments", DomainMessageKeys.DemoStaffRealPaymentsDisabled)]
    [InlineData("export", DomainMessageKeys.DemoRealExportDisabled)]
    [InlineData("delete-account", DomainMessageKeys.DemoDeleteAccountDisabled)]
    public void Each_guard_returns_a_failure_with_its_message_key(string guard, string expectedKey)
    {
        var result = guard switch
        {
            "password-reset" => DemoGuards.DemoPasswordResetBlocked(),
            "password-change" => DemoGuards.DemoPasswordChangeBlocked(),
            "email" => DemoGuards.DemoEmailSendingBlocked(),
            "payments" => DemoGuards.DemoRealPaymentsBlocked(),
            "export" => DemoGuards.DemoExportBlocked(),
            "delete-account" => DemoGuards.DemoDeleteAccountBlocked(),
            _ => throw new ArgumentOutOfRangeException(nameof(guard), guard, "Unknown guard")
        };

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal("Validation", result.Error!.Code);
        Assert.Equal(expectedKey, result.Error.MessageKey);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
    }

    // ── Enforcement at call sites: demo refused, real user allowed ──────────────────────────

    [Fact]
    public async Task Password_reset_request_refuses_the_demo_user_and_still_allows_a_real_one()
    {
        var auth = CreateAuthService();

        var demo = await auth.ResetPasswordAsync(new ResetPasswordRequest { Email = DemoEmail });
        Assert.False(demo.IsSuccess);
        Assert.Equal(DomainMessageKeys.DemoPasswordResetDisabled, demo.Error!.MessageKey);
        Assert.False(await _context.PasswordResetTokens.AnyAsync(t => t.UserId == _demoUserId));

        var real = await auth.ResetPasswordAsync(new ResetPasswordRequest { Email = RealEmail });
        Assert.True(real.IsSuccess);
        Assert.True(await _context.PasswordResetTokens.AnyAsync(t => t.UserId == _realUserId));
    }

    [Fact]
    public async Task Confirm_reset_refuses_the_demo_user_and_still_changes_a_real_users_password()
    {
        var auth = CreateAuthService();

        var demoToken = "demo-raw-token";
        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = _demoUserId,
            TokenHash = TokenHasher.Hash(demoToken),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false
        });
        var realToken = "real-raw-token";
        _context.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = _realUserId,
            TokenHash = TokenHasher.Hash(realToken),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false
        });
        await _context.SaveChangesAsync();

        var demoUserBefore = (await _context.Users.FindAsync(_demoUserId))!.PasswordHash;
        var realUserBefore = (await _context.Users.FindAsync(_realUserId))!.PasswordHash;

        var demo = await auth.ConfirmResetPasswordAsync(
            new ConfirmResetPasswordRequest { Token = demoToken, NewPassword = "NewPassword1!" });
        Assert.False(demo.IsSuccess);
        Assert.Equal(DomainMessageKeys.DemoPasswordChangeDisabled, demo.Error!.MessageKey);
        Assert.Equal(demoUserBefore, (await _context.Users.FindAsync(_demoUserId))!.PasswordHash);

        var real = await auth.ConfirmResetPasswordAsync(
            new ConfirmResetPasswordRequest { Token = realToken, NewPassword = "NewPassword1!" });
        Assert.True(real.IsSuccess);
        Assert.NotEqual(realUserBefore, (await _context.Users.FindAsync(_realUserId))!.PasswordHash);
    }

    [Fact]
    public async Task Recording_a_salary_payment_is_refused_for_the_demo_user_and_allowed_for_a_real_one()
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = _accountId, Name = "Farm" };
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
        _context.Farms.Add(farm);
        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        var currentUser = new SwitchableCurrentUser(_demoUserId);
        var service = new EmployeeService(_context, currentUser);

        var demo = await service.RecordSalaryPaymentAsync(
            farm.Id, employee.Id, new RecordSalaryPaymentRequest { Amount = 50000 });
        Assert.False(demo.IsSuccess);
        Assert.Equal(DomainMessageKeys.DemoStaffRealPaymentsDisabled, demo.Error!.MessageKey);
        Assert.False(await _context.SalaryPayments.AnyAsync(p => p.EmployeeId == employee.Id));

        currentUser.UserId = _realUserId;
        var real = await service.RecordSalaryPaymentAsync(
            farm.Id, employee.Id, new RecordSalaryPaymentRequest { Amount = 50000 });
        Assert.True(real.IsSuccess);
        Assert.True(await _context.SalaryPayments.AnyAsync(p => p.EmployeeId == employee.Id));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private AuthService CreateAuthService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://farm.example.com",
                ["Demo:DemoEmail"] = DemoEmail,
                ["Jwt:SecretKey"] = "TestSecretKeyThatIsAtLeast32BytesLong!!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:AccessTokenExpiryMinutes"] = "60",
                ["Jwt:RefreshTokenExpiryDays"] = "30"
            })
            .Build();

        return new AuthService(
            _context,
            new JwtTokenService(configuration),
            configuration,
            new NoopEmailService(),
            NullLogger<AuthService>.Instance);
    }

    private sealed class SwitchableCurrentUser : ICurrentUserService
    {
        public SwitchableCurrentUser(Guid userId) => UserId = userId;

        public Guid UserId { get; set; }
        public Guid? GetUserId() => UserId;
        public string? GetUserEmail() => null;
    }

    private sealed class NoopEmailService : IEmailService
    {
        public Task SendPasswordResetEmailAsync(string email, string resetLink) => Task.CompletedTask;

        public Task SendFarmInvitationEmailAsync(string email, string farmName, string inviteLink) =>
            Task.CompletedTask;

        public Task SendAlertNotificationsEmailAsync(
            string email,
            string farmName,
            IReadOnlyList<FMS.Application.Auth.NotificationEmailItem> notifications) =>
            Task.CompletedTask;
    }
}
