using FMS.Application.Auth;
using FMS.Application.Email;
using FMS.Application.Farm;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Farm;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FMS.Domain.Tests.Email;

/// <summary>
/// The design decision this file exists to defend: switching on a real email provider
/// must not make password reset or invitation *stricter* than the alert digest already
/// was. In subphase 3.3 a failed digest send logs and records the error, and the run
/// continues; if a reset request started returning 500 because SendGrid was down, the
/// same failure would be handled two different ways in one application.
///
/// <para>
/// Both flows persist their durable state — the reset token, the invitation row —
/// <em>before</em> the send, so a delivery failure can only mean "nobody got the mail",
/// never "the token was lost". These drive the real services with a transport whose every
/// send throws, and assert exactly that: the caller succeeds, the row is there, and the
/// failure is visible in the log rather than swallowed.
/// </para>
///
/// <para>
/// Scope note: this is service-level, not end-to-end. The test host registers a capturing
/// <c>IEmailService</c> and exposes no service-override hook, so proving the throw path
/// over HTTP would mean changing the shared doubles that three existing features depend
/// on — see the no-diff assertion in <c>EmailConfigurationDriftTests</c>. What is proven
/// here is the production caller behaviour; the controller layer adds no error handling
/// of its own on top of <c>Result</c>.
/// </para>
/// </summary>
public class EmailCallerContinuationTests : IDisposable
{
    private readonly FmsDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly CapturingLoggerProvider _authLogs;
    private readonly CapturingLoggerProvider _membershipLogs;

    private readonly Guid _accountId = Guid.NewGuid();
    private readonly Guid _farmId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();

    private const string OwnerEmail = "owner@example.com";
    private const string UserEmail = "farmer@example.com";

    public EmailCallerContinuationTests()
    {
        _context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://farm.example.com",
                ["Invitations:ExpiryDays"] = "7",
                ["Jwt:SecretKey"] = "TestSecretKeyThatIsAtLeast32BytesLong!!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:AccessTokenExpiryMinutes"] = "60",
                ["Jwt:RefreshTokenExpiryDays"] = "30"
            })
            .Build();

        var (authLogger, authLogs) = TestLoggers.Create<AuthService>();
        var (membershipLogger, membershipLogs) = TestLoggers.Create<FarmMembershipService>();
        _authLogs = authLogs;
        _membershipLogs = membershipLogs;

        // A transport whose every send fails the way an outage or a rejected key does.
        var emailService = new EmailService(new AlwaysFailingTransport(), _configuration);

        Auth = new AuthService(_context, new JwtTokenService(_configuration), _configuration, emailService, authLogger);
        Membership = new FarmMembershipService(
            _context, emailService, new JwtTokenService(_configuration), _configuration, membershipLogger);

        Seed();
    }

    public AuthService Auth { get; }

    public FarmMembershipService Membership { get; }

    private sealed class AlwaysFailingTransport : IEmailTransport
    {
        public int Attempts { get; private set; }

        public Exception Failure { get; set; } =
            new EmailDeliveryException("(test) SendGrid rejected the message with 401 Unauthorized", isTransient: false, 401);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw Failure;
        }
    }

    private void Seed()
    {
        _context.Accounts.Add(new Account { Id = _accountId, Name = "Account", CreatedAt = DateTime.UtcNow });

        _context.Users.Add(new User
        {
            Id = _ownerId,
            AccountId = _accountId,
            Email = OwnerEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Passw0rd"),
            FirstName = "Owner",
            LastName = "One",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        _context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            AccountId = _accountId,
            Email = UserEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Passw0rd"),
            FirstName = "Farmer",
            LastName = "One",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        _context.Farms.Add(new Farm
        {
            Id = _farmId,
            AccountId = _accountId,
            Name = "Green Acres",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        _context.UserFarms.Add(new UserFarm
        {
            Id = Guid.NewGuid(),
            UserId = _ownerId,
            FarmId = _farmId,
            Role = FarmRoles.SystemOwner,
            CreatedAt = DateTime.UtcNow
        });

        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _authLogs.Dispose();
        _membershipLogs.Dispose();
    }

    // ── password reset ─────────────────────────────────────

    [Fact]
    public async Task ResetPassword_WhenTheSendThrows_StillSucceedsAndKeepsTheToken()
    {
        var result = await Auth.ResetPasswordAsync(new ResetPasswordRequest { Email = UserEmail });

        Assert.True(result.IsSuccess, $"a failed email must not fail the request: {result.Error}");

        var token = await _context.PasswordResetTokens.AsNoTracking().SingleAsync();
        Assert.False(token.IsUsed);
        Assert.True(token.ExpiresAt > DateTime.UtcNow, "the token must still be usable");
    }

    [Fact]
    public async Task ResetPassword_WhenTheSendThrows_LogsTheFailureWithTheUserId()
    {
        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Email == UserEmail);

        await Auth.ResetPasswordAsync(new ResetPasswordRequest { Email = UserEmail });

        var error = Assert.Single(_authLogs.ForLevel(LogLevel.Error));
        Assert.Contains(user.Id.ToString(), error.Message);
        // Says the durable part survived, so an operator does not assume the reset is lost.
        Assert.Contains("retry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<EmailDeliveryException>(error.Exception);
    }

    [Fact]
    public async Task ResetPassword_ForAnUnknownAddress_NeitherSendsNorFails()
    {
        // The enumeration guard: the send only happens for addresses that exist, so a
        // delivery failure here must never become the thing that reveals which is which.
        var result = await Auth.ResetPasswordAsync(new ResetPasswordRequest { Email = "nobody@example.com" });

        Assert.True(result.IsSuccess);
        Assert.Empty(await _context.PasswordResetTokens.AsNoTracking().ToListAsync());
        Assert.Empty(_authLogs.ForLevel(LogLevel.Error));
    }

    [Fact]
    public async Task ResetPassword_WithAWorkingSend_IsUnaffected()
    {
        // The continuation must not have been bought by never sending.
        var transport = new CapturingTransport();
        var service = new EmailService(transport, _configuration);
        var (logger, _) = TestLoggers.Create<AuthService>();
        var auth = new AuthService(_context, new JwtTokenService(_configuration), _configuration, service, logger);

        var result = await auth.ResetPasswordAsync(new ResetPasswordRequest { Email = UserEmail });

        Assert.True(result.IsSuccess);
        var message = Assert.Single(transport.Sent);
        Assert.Equal(UserEmail, message.To);
        // The link the recipient needs, against the configured frontend URL.
        Assert.Contains("https://farm.example.com/confirm-reset-password?token=", message.TextBody);
    }

    [Fact]
    public async Task ResetPassword_WhenTheTransportThrowsSomethingUnexpected_StillSucceeds()
    {
        // The catch is deliberately broad for the same reason the dispatcher's is: a
        // provider client can fail in ways we did not enumerate, and none of them should
        // turn into a 500 on the login page.
        var transport = new AlwaysFailingTransport { Failure = new OutOfMemoryException("(test) provider client blew up") };
        var service = new EmailService(transport, _configuration);
        var (logger, logs) = TestLoggers.Create<AuthService>();
        var auth = new AuthService(_context, new JwtTokenService(_configuration), _configuration, service, logger);

        var result = await auth.ResetPasswordAsync(new ResetPasswordRequest { Email = UserEmail });

        Assert.True(result.IsSuccess);
        Assert.Single(logs.ForLevel(LogLevel.Error));
    }

    // ── farm invitation ────────────────────────────────────

    [Fact]
    public async Task Invite_WhenTheSendThrows_StillPersistsTheInvitationAndSucceeds()
    {
        var result = await Membership.InviteAsync(_farmId, _ownerId, new InviteMemberRequest
        {
            Email = "invitee@example.com",
            Role = FarmRoles.Employee
        });

        Assert.True(result.IsSuccess, $"a failed email must not fail the invitation: {result.Error}");

        var invitation = await _context.FarmInvitations.AsNoTracking().SingleAsync();
        Assert.Equal("invitee@example.com", invitation.Email);
        Assert.Equal(FarmRoles.Employee, invitation.Role);
        // Still pending, so it can be revoked or re-sent rather than being lost.
        Assert.Equal(FarmInvitationStatus.Pending, invitation.Status);
        Assert.True(invitation.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Invite_WhenTheSendThrows_ReturnsTheInvitationAsIfItHadBeenSent()
    {
        var result = await Membership.InviteAsync(_farmId, _ownerId, new InviteMemberRequest
        {
            Email = "invitee@example.com",
            Role = FarmRoles.Viewer
        });

        // The client shows the pending invitation row; a recipient not receiving the mail
        // is recoverable, a request that half-succeeds is not.
        Assert.NotNull(result.Value);
        Assert.Equal("invitee@example.com", result.Value!.Email);
    }

    [Fact]
    public async Task Invite_WhenTheSendThrows_LogsTheFailureWithTheFarmAndRecipient()
    {
        await Membership.InviteAsync(_farmId, _ownerId, new InviteMemberRequest
        {
            Email = "invitee@example.com",
            Role = FarmRoles.Employee
        });

        var error = Assert.Single(_membershipLogs.ForLevel(LogLevel.Error));
        Assert.Contains(_farmId.ToString(), error.Message);
        Assert.Contains("invitee@example.com", error.Message);
        Assert.Contains("revoked or re-sent", error.Message);
        Assert.IsType<EmailDeliveryException>(error.Exception);
    }

    [Fact]
    public async Task Invite_WithAWorkingSend_DeliversTheLinkAndExpiry()
    {
        var transport = new CapturingTransport();
        var service = new EmailService(transport, _configuration);
        var (logger, _) = TestLoggers.Create<FarmMembershipService>();
        var membership = new FarmMembershipService(
            _context, service, new JwtTokenService(_configuration), _configuration, logger);

        var result = await membership.InviteAsync(_farmId, _ownerId, new InviteMemberRequest
        {
            Email = "invitee@example.com",
            Role = FarmRoles.Employee
        });

        Assert.True(result.IsSuccess);
        var message = Assert.Single(transport.Sent);
        Assert.Equal("invitee@example.com", message.To);
        Assert.Contains("Green Acres", message.Subject);
        Assert.Contains("https://farm.example.com/accept-invitation?token=", message.TextBody);
        Assert.Contains("expires in 7 days", message.TextBody);
    }

    private sealed class CapturingTransport : IEmailTransport
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
