using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Application.Auth;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Farm membership and the invitation flow.
///
/// Uses the REAL FarmContextMiddleware so membership enforcement is genuine: that
/// is what produces the "Access denied to this farm" body the client keys its
/// mid-session revocation handling on.
/// </summary>
public class FarmInvitationE2ETests : IClassFixture<FarmInvitationE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        public Guid FarmAId { get; private set; }
        public Guid FarmGId { get; private set; }

        public Guid OwnerUserId { get; private set; }
        public Guid ViewerUserId { get; private set; }
        public Guid InviteeUserId { get; private set; }
        public Guid RevokeeUserId { get; private set; }
        public Guid DeclineeUserId { get; private set; }
        public Guid ExpiredUserId { get; private set; }
        public Guid RevokedUserId { get; private set; }
        public Guid SameAccountNonMemberUserId { get; private set; }
        public Guid StaleHeaderUserId { get; private set; }
        public Guid StrangerUserId { get; private set; }

        public const string OwnerEmail = "owner.a@example.com";
        public const string ViewerEmail = "viewer.a@example.com";
        public const string InviteeEmail = "invitee.b@example.com";
        public const string RevokeeEmail = "revokee.b@example.com";
        public const string DeclineeEmail = "declinee.b@example.com";
        public const string ExpiredEmail = "expired.b@example.com";
        public const string RevokedEmail = "revoked.b@example.com";
        public const string SameAccountNonMemberEmail = "loner.a@example.com";
        public const string StaleHeaderEmail = "staleheader.b@example.com";
        public const string StrangerEmail = "stranger.c@example.com";

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            // Account A owns the two farms used here. The seeded test user is left
            // untouched so the pre-existing suites keep working.
            var accountA = new Account { Id = Guid.NewGuid(), Name = "Account A", CreatedAt = DateTime.UtcNow };
            var accountB = new Account { Id = Guid.NewGuid(), Name = "Account B", CreatedAt = DateTime.UtcNow };
            var accountC = new Account { Id = Guid.NewGuid(), Name = "Account C", CreatedAt = DateTime.UtcNow };
            db.Accounts.AddRange(accountA, accountB, accountC);

            FarmAId = Guid.NewGuid();
            FarmGId = Guid.NewGuid();
            db.Farms.Add(new Farm { Id = FarmAId, AccountId = accountA.Id, Name = "Farm A", IsActive = true, CreatedAt = DateTime.UtcNow });
            db.Farms.Add(new Farm { Id = FarmGId, AccountId = accountA.Id, Name = "Farm G", IsActive = true, CreatedAt = DateTime.UtcNow });

            OwnerUserId = AddUser(db, accountA.Id, OwnerEmail, "Owner", "A");
            ViewerUserId = AddUser(db, accountA.Id, ViewerEmail, "Viewer", "A");
            SameAccountNonMemberUserId = AddUser(db, accountA.Id, SameAccountNonMemberEmail, "Loner", "A");

            InviteeUserId = AddUser(db, accountB.Id, InviteeEmail, "Invitee", "B");
            RevokeeUserId = AddUser(db, accountB.Id, RevokeeEmail, "Revokee", "B");
            DeclineeUserId = AddUser(db, accountB.Id, DeclineeEmail, "Declinee", "B");
            ExpiredUserId = AddUser(db, accountB.Id, ExpiredEmail, "Expired", "B");
            RevokedUserId = AddUser(db, accountB.Id, RevokedEmail, "Revoked", "B");
            StaleHeaderUserId = AddUser(db, accountB.Id, StaleHeaderEmail, "Stale", "B");

            StrangerUserId = AddUser(db, accountC.Id, StrangerEmail, "Stranger", "C");

            // Farm A: owner + a non-admin viewer. Farm G: owner only (last-owner guard).
            AddMembership(db, OwnerUserId, FarmAId, "SystemOwner");
            AddMembership(db, ViewerUserId, FarmAId, "Viewer");
            AddMembership(db, OwnerUserId, FarmGId, "SystemOwner");

            await db.SaveChangesAsync();
        }

        private static Guid AddUser(FmsDbContext db, Guid accountId, string email, string first, string last)
        {
            var id = Guid.NewGuid();
            db.Users.Add(new User
            {
                Id = id,
                AccountId = accountId,
                Email = email,
                PasswordHash = "not-used-tests-authenticate-with-generated-tokens",
                FirstName = first,
                LastName = last,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            return id;
        }

        private static void AddMembership(FmsDbContext db, Guid userId, Guid farmId, string role) =>
            db.UserFarms.Add(new UserFarm
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FarmId = farmId,
                Role = role,
                CreatedAt = DateTime.UtcNow
            });
    }

    private sealed class FarmListDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? UserFarmRole { get; set; }
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public FarmInvitationE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;

        // Touch the host so seeding has run before a test reads the factory's
        // seeded ids. Those ids are arguments to ClientFor, so they are evaluated
        // before the call that would otherwise trigger seeding.
        _ = _factory.Host;
    }

    public void Dispose() { }

    private HttpClient ClientFor(Guid userId, string email, Guid? headerFarm = null)
    {
        _ = _factory.Host; // ensure seeded
        var client = _factory.CreateClient();
        var token = JwtTokenHelper.GenerateTestToken(userId, new[] { "SystemOwner" }, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (headerFarm.HasValue)
            client.DefaultRequestHeaders.Add("X-Farm-Id", headerFarm.Value.ToString());
        return client;
    }

    /// <summary>The invitation token captured from the (placeholder) email service.</summary>
    private string LastInvitationToken =>
        ((PasswordResetE2ETests.TestEmailService)_factory.Services.GetRequiredService<IEmailService>()).LastToken
        ?? throw new InvalidOperationException("No invitation token was captured");

    private async Task<string> InviteAsync(HttpClient ownerClient, Guid farmId, string email, string role)
    {
        var response = await ownerClient.PostAsJsonAsync(
            $"/api/farm/{farmId}/invitations", new { email, role });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return LastInvitationToken;
    }

    private async Task<UserFarm?> FindMembershipAsync(Guid userId, Guid farmId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await db.UserFarms.FirstOrDefaultAsync(uf => uf.UserId == userId && uf.FarmId == farmId);
    }

    // ── Check 1: cross-account invite → accept → farm appears ────────────────

    [Fact]
    public async Task CrossAccountInvite_Accept_AddsTheFarmToTheInviteesFarmList()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);
        var invitee = ClientFor(_factory.InviteeUserId, Factory.InviteeEmail);

        // Mixed-case invite also proves email matching is case-insensitive.
        var token = await InviteAsync(owner, _factory.FarmAId, "Invitee.B@Example.COM", "Veterinarian");

        var before = await invitee.GetFromJsonAsync<List<FarmListDto>>("/api/farms");
        Assert.DoesNotContain(before!, f => f.Id == _factory.FarmAId);

        var accept = await invitee.PostAsJsonAsync("/api/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var after = await invitee.GetFromJsonAsync<List<FarmListDto>>("/api/farms");
        var joined = Assert.Single(after!, f => f.Id == _factory.FarmAId);
        Assert.Equal("Veterinarian", joined.UserFarmRole);

        _output.WriteLine($"invitee farms after accept: {after!.Count}");
    }

    // ── Check 1 (continued): no leak from dropping the AccountId filter ──────

    [Fact]
    public async Task FarmList_DoesNotExposeFarmsWithoutMembershipEvenInTheSameAccount()
    {
        var loner = ClientFor(_factory.SameAccountNonMemberUserId, Factory.SameAccountNonMemberEmail);

        var farms = await loner.GetFromJsonAsync<List<FarmListDto>>("/api/farms");

        // Same account as Farm A / Farm G, but never invited: nothing may leak.
        Assert.NotNull(farms);
        Assert.Empty(farms!);
    }

    // ── Check 2: last SystemOwner cannot be removed or demoted ──────────────

    [Fact]
    public async Task RemovingTheLastOwner_IsBlocked_AndTheMembershipSurvives()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmGId);

        var remove = await owner.DeleteAsync($"/api/farm/{_factory.FarmGId}/members/{_factory.OwnerUserId}");
        _output.WriteLine($"remove last owner -> {remove.StatusCode}");
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);

        var demote = await owner.PutAsJsonAsync(
            $"/api/farm/{_factory.FarmGId}/members/{_factory.OwnerUserId}/role", new { role = "Viewer" });
        _output.WriteLine($"demote last owner -> {demote.StatusCode}");
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);

        var membership = await FindMembershipAsync(_factory.OwnerUserId, _factory.FarmGId);
        Assert.NotNull(membership);
        Assert.Equal("SystemOwner", membership!.Role);
    }

    // ── Check 3: accept requires the invitation's email ─────────────────────

    [Fact]
    public async Task Accept_WithADifferentEmail_IsRejected_AndDoesNotConsumeTheToken()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmGId);
        var token = await InviteAsync(owner, _factory.FarmGId, Factory.InviteeEmail, "Employee");

        var stranger = ClientFor(_factory.StrangerUserId, Factory.StrangerEmail);
        var attempt = await stranger.PostAsJsonAsync("/api/invitations/accept", new { token });

        _output.WriteLine($"mismatched email accept -> {attempt.StatusCode}");
        Assert.Equal(HttpStatusCode.BadRequest, attempt.StatusCode);
        Assert.Null(await FindMembershipAsync(_factory.StrangerUserId, _factory.FarmGId));

        // The rightful invitee can still redeem the same token.
        var invitee = ClientFor(_factory.InviteeUserId, Factory.InviteeEmail);
        var accept = await invitee.PostAsJsonAsync("/api/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }

    // ── Check 4: the invitation skip must not exempt farm-scoped routes ─────

    [Fact]
    public async Task InvitationSkip_DoesNotExemptFarmScopedRoutes()
    {
        var loner = ClientFor(_factory.SameAccountNonMemberUserId, Factory.SameAccountNonMemberEmail, _factory.FarmAId);

        // A farm-scoped route that merely contains "invitations" in the family is
        // still membership-enforced.
        var farmScoped = await loner.GetAsync($"/api/farm/{_factory.FarmAId}/members");
        _output.WriteLine($"non-member farm-scoped -> {farmScoped.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, farmScoped.StatusCode);
        Assert.Contains("Access denied to this farm", await farmScoped.Content.ReadAsStringAsync());

        var invitationsRoute = await loner.GetAsync($"/api/farm/{_factory.FarmAId}/invitations");
        Assert.Equal(HttpStatusCode.Forbidden, invitationsRoute.StatusCode);

        // The skipped account-scoped path is reached (validation, not a 403).
        var acceptBogus = await loner.PostAsJsonAsync("/api/invitations/accept", new { token = "not-a-real-token" });
        _output.WriteLine($"skipped accept path -> {acceptBogus.StatusCode}");
        Assert.Equal(HttpStatusCode.BadRequest, acceptBogus.StatusCode);
    }

    [Fact]
    public async Task Accept_WorksEvenWithAStaleFarmHeader()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);
        var token = await InviteAsync(owner, _factory.FarmAId, Factory.StaleHeaderEmail, "Viewer");

        // The invitee's browser may still carry an X-Farm-Id for a farm they left;
        // it must not 403 the accept call.
        var invitee = ClientFor(_factory.StaleHeaderUserId, Factory.StaleHeaderEmail, _factory.FarmAId);
        var response = await invitee.PostAsJsonAsync("/api/invitations/accept", new { token });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Check 6 (backend half): revocation denies the next request ──────────

    [Fact]
    public async Task RevokedMembership_NextFarmScopedRequest_IsRejectedWithTheFarmContextBody()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);
        var revokee = ClientFor(_factory.RevokeeUserId, Factory.RevokeeEmail);

        var token = await InviteAsync(owner, _factory.FarmAId, Factory.RevokeeEmail, "Employee");
        var accept = await revokee.PostAsJsonAsync("/api/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var memberClient = ClientFor(_factory.RevokeeUserId, Factory.RevokeeEmail, _factory.FarmAId);
        var whileMember = await memberClient.GetAsync($"/api/farm/{_factory.FarmAId}/members");
        Assert.Equal(HttpStatusCode.OK, whileMember.StatusCode);

        var remove = await owner.DeleteAsync($"/api/farm/{_factory.FarmAId}/members/{_factory.RevokeeUserId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var afterRevoke = await memberClient.GetAsync($"/api/farm/{_factory.FarmAId}/members");
        _output.WriteLine($"after revocation -> {afterRevoke.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, afterRevoke.StatusCode);

        // Exactly the body the client's interceptor looks for.
        var body = await afterRevoke.Content.ReadAsStringAsync();
        Assert.Contains("Access denied to this farm", body);
    }

    // ── Remaining guards ─────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_ByANonAdminMember_IsForbidden()
    {
        var viewer = ClientFor(_factory.ViewerUserId, Factory.ViewerEmail, _factory.FarmAId);

        var response = await viewer.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmAId}/invitations", new { email = "newcomer@example.com", role = "Viewer" });

        _output.WriteLine($"non-admin invite -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invite_ForAnExistingMember_IsRejected()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);

        var response = await owner.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmAId}/invitations", new { email = Factory.ViewerEmail, role = "Viewer" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invite_WithAnUnknownRole_IsRejected()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);

        var response = await owner.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmAId}/invitations", new { email = "someone@example.com", role = "SuperAdmin" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Decline_LeavesTheUserWithoutMembership()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);
        var declinee = ClientFor(_factory.DeclineeUserId, Factory.DeclineeEmail);
        var token = await InviteAsync(owner, _factory.FarmAId, Factory.DeclineeEmail, "Viewer");

        var decline = await declinee.PostAsJsonAsync("/api/invitations/decline", new { token });
        Assert.Equal(HttpStatusCode.OK, decline.StatusCode);

        Assert.Null(await FindMembershipAsync(_factory.DeclineeUserId, _factory.FarmAId));
    }

    [Fact]
    public async Task Accept_WithAnExpiredInvitation_IsRejected()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);
        var expiredUser = ClientFor(_factory.ExpiredUserId, Factory.ExpiredEmail);
        var token = await InviteAsync(owner, _factory.FarmAId, Factory.ExpiredEmail, "Viewer");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            var invitation = await db.FarmInvitations.FirstAsync(i => i.FarmId == _factory.FarmAId
                && i.Email == Factory.ExpiredEmail);
            invitation.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var accept = await expiredUser.PostAsJsonAsync("/api/invitations/accept", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, accept.StatusCode);
        Assert.Null(await FindMembershipAsync(_factory.ExpiredUserId, _factory.FarmAId));
    }

    [Fact]
    public async Task Accept_AfterTheInvitationIsRevoked_IsRejected()
    {
        var owner = ClientFor(_factory.OwnerUserId, Factory.OwnerEmail, _factory.FarmAId);
        var revokedUser = ClientFor(_factory.RevokedUserId, Factory.RevokedEmail);
        var token = await InviteAsync(owner, _factory.FarmAId, Factory.RevokedEmail, "Viewer");

        var pending = await owner.GetFromJsonAsync<List<InvitationDto>>($"/api/farm/{_factory.FarmAId}/invitations");
        var invitation = Assert.Single(pending!, i => i.Email == Factory.RevokedEmail);

        var revoke = await owner.DeleteAsync($"/api/farm/{_factory.FarmAId}/invitations/{invitation.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var accept = await revokedUser.PostAsJsonAsync("/api/invitations/accept", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, accept.StatusCode);
        Assert.Null(await FindMembershipAsync(_factory.RevokedUserId, _factory.FarmAId));
    }

    private sealed class InvitationDto
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
