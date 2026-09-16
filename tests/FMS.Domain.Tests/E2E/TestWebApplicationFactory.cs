using System.Security.Claims;
using System.Text;
using FMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Creates a test server with InMemory database and a test auth scheme that always succeeds.
/// Uses IHost + TestServer to avoid the internal Program class issue with WebApplicationFactory.
/// </summary>
public class TestWebApplicationFactory : IDisposable
{
    private IHost? _host;

    // Generated once so every scope/request shares the same InMemory store.
    private readonly string _inMemoryDbName = $"TestDb_{Guid.NewGuid()}";

    public IHost Host => _host ??= CreateHost();

    public IServiceProvider Services => Host.Services;

    public HttpClient CreateClient() => Host.GetTestClient();

    /// <summary>
    /// When true, the test host runs the real FMS.API FarmContextMiddleware
    /// (X-Farm-Id membership validation + route/header farm enforcement)
    /// instead of the permissive inline stub. Override in security-focused
    /// test factories; default false keeps legacy suite behavior.
    /// </summary>
    protected virtual bool UsesRealFarmContextMiddleware => false;

    public HttpClient CreateAuthenticatedClient(Guid? farmId = null)
    {
        var client = Host.GetTestClient();
        var token = JwtTokenHelper.GenerateTestToken();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (farmId.HasValue)
            client.DefaultRequestHeaders.Add("X-Farm-Id", farmId.Value.ToString());
        return client;
    }

    private IHost CreateHost()
    {
        var builder = new HostBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frontend:BaseUrl"] = "http://localhost:3000",
                    ["Jwt:RefreshTokenExpiryDays"] = "30",
                    ["Jwt:SecretKey"] = "TestSecretKeyThatIsAtLeast32BytesLong!!",
                    ["Jwt:Issuer"] = "TestIssuer",
                    ["Jwt:Audience"] = "TestAudience",
                    ["Jwt:AccessTokenExpiryMinutes"] = "60"
                });
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    if (UsesRealFarmContextMiddleware)
                    {
                        // Exercise the REAL production middleware: membership validation
                        // and route/header farm enforcement, exactly as in Program.cs.
                        app.UseMiddleware<FMS.API.Middleware.FarmContextMiddleware>();
                    }
                    else
                    {
                        // Legacy permissive inline stub: reads X-Farm-Id without
                        // membership validation so existing suites keep passing.
                        app.Use(async (context, next) =>
                        {
                            var farmSvc = context.RequestServices.GetRequiredService<FMS.Application.Farm.IFarmContextService>();
                            if (context.Request.Headers.TryGetValue("X-Farm-Id", out var fid) && Guid.TryParse(fid.ToString(), out var farmId))
                                farmSvc.SetCurrentFarmId(farmId);
                            await next();
                        });
                    }
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
                webBuilder.ConfigureServices(services =>
                {
                    // Remove existing DbContext
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<FmsDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    var dbContextDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(FmsDbContext));
                    if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);

                    // Add test database — provider chosen by ConfigureDbContextOptions
                    services.AddDbContext<FmsDbContext>(ConfigureDbContextOptions);

                    // Add controllers from the API assembly
                    services.AddControllers()
                        .AddApplicationPart(typeof(FMS.API.Controllers.DashboardController).Assembly)
                        .AddJsonOptions(o =>
                        {
                            o.JsonSerializerOptions.Converters.Add(
                                new System.Text.Json.Serialization.JsonStringEnumConverter());
                        });

                    // Add FarmContext service
                    services.AddScoped<FMS.Application.Farm.IFarmContextService, FMS.Application.Farm.FarmContext>();
                    services.AddHttpContextAccessor();

                    // Register all application services needed by controllers
                    services.AddScoped<FMS.Application.Common.ICurrentUserService, FMS.Infrastructure.Auth.CurrentUserService>();
                    services.AddScoped<FMS.Application.Inventory.IInventoryService, FMS.Infrastructure.Inventory.InventoryService>();
                    services.AddScoped<FMS.Application.Feed.IFeedService, FMS.Infrastructure.Feed.FeedService>();
                    services.AddScoped<FMS.Application.Finance.IFinanceService, FMS.Infrastructure.Finance.FinanceService>();
                    services.AddScoped<FMS.Application.Health.IVaccineService, FMS.Infrastructure.Health.VaccineService>();
                    services.AddScoped<FMS.Application.Health.IMedicineService, FMS.Infrastructure.Health.MedicineService>();
                    services.AddScoped<FMS.Application.Health.IMedicalRecordService, FMS.Infrastructure.Health.MedicalRecordService>();
                    services.AddScoped<FMS.Application.Health.IHealthCostService, FMS.Infrastructure.Health.HealthCostService>();
                    services.AddScoped<FMS.Application.Health.IWeightCheckScheduleService, FMS.Infrastructure.Health.WeightCheckScheduleService>();
                    services.AddScoped<FMS.Application.Tasks.IFarmTaskService, FMS.Infrastructure.Tasks.FarmTaskService>();
                    services.AddScoped<FMS.Application.Animal.IAnimalService, FMS.Infrastructure.Animals.AnimalService>();
                    services.AddScoped<FMS.Application.Breeding.IBreedingService, FMS.Infrastructure.Breeding.BreedingService>();
                    services.AddScoped<FMS.Application.Employees.IEmployeeService, FMS.Infrastructure.Employees.EmployeeService>();
                    services.AddScoped<FMS.Application.Dashboard.IDashboardService, FMS.Infrastructure.Dashboard.DashboardService>();
                    services.AddScoped<FMS.Application.Reports.IReportService, FMS.Infrastructure.Reports.ReportService>();
                    services.AddScoped<FMS.Application.AuditLog.IAuditLogService, FMS.Infrastructure.AuditLog.AuditLogService>();
                    services.AddScoped<FMS.Application.Auth.IAuthService, FMS.Infrastructure.Auth.AuthService>();
                    services.AddScoped<FMS.Application.Auth.IJwtTokenService, FMS.Infrastructure.Auth.JwtTokenService>();
                    // Singleton so all request scopes share the same capturing instance.
                    services.AddSingleton<FMS.Application.Auth.IEmailService, PasswordResetE2ETests.TestEmailService>();

                    // Simple test auth: scheme "Test" that always authenticates with the claims from the token
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });
                    services.AddAuthorization();
                });
            });

        var host = builder.Start();

        // Seed data directly into the host's service provider
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
            db.Database.EnsureCreated();
            var baseSeed = ApiSeedData.SeedAsync(db).GetAwaiter().GetResult();
            SeedAsync(db, baseSeed).GetAwaiter().GetResult();
            SeedData = baseSeed;
        }

        return host;
    }

    /// <summary>
    /// Configures the FmsDbContext options for the test host. The default
    /// implementation uses the EF Core InMemory provider (shared database name
    /// across scopes). Override to target a real database provider, e.g.
    /// UseNpgsql for PostgreSQL integration tests.
    /// </summary>
    protected virtual void ConfigureDbContextOptions(DbContextOptionsBuilder options)
    {
        // InMemory requires a shared database name so all scopes see the same data.
        options.UseInMemoryDatabase(_inMemoryDbName);
    }

    /// <summary>
    /// Hook for subclasses to seed additional data (e.g. extra farms/users)
    /// after the standard ApiSeedData run. Default: no-op.
    /// </summary>
    protected virtual Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed) => Task.CompletedTask;

    public ApiSeedData.SeedIds SeedData { get; private set; } = null!;

    public void Dispose()
    {
        _host?.Dispose();
    }
}

/// <summary>
/// A test authentication handler that always authenticates with fixed claims.
/// Reads the Bearer token and extracts the NameIdentifier claim from it.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Extract the Bearer token
        string? token = null;
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var header = authHeader.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = header.Substring("Bearer ".Length).Trim();
        }

        // Reject if no token provided
        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, JwtTokenHelper.TestUserId.ToString()),
            new(ClaimTypes.Name, "testuser@test.com"),
            new(ClaimTypes.Role, "FarmManager"),
        };

        // Extract claims from the JWT so tests can exercise role-based authorization.
        // Defaults to FarmManager when no token claims are present (backwards compatible).
        try
        {
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
            var jwtClaims = jwt.Claims.ToList();

            var nameIdClaim = jwtClaims.FirstOrDefault(c =>
                c.Type == "sub" ||
                c.Type == ClaimTypes.NameIdentifier ||
                c.Type == "nameid");
            if (nameIdClaim != null)
                claims[0] = new Claim(ClaimTypes.NameIdentifier, nameIdClaim.Value);

            var nameClaim = jwtClaims.FirstOrDefault(c =>
                c.Type == ClaimTypes.Name || c.Type == "unique_name" || c.Type == "email");
            if (nameClaim != null)
                claims[1] = new Claim(ClaimTypes.Name, nameClaim.Value);

            var roleClaims = jwtClaims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value)
                .Distinct()
                .ToList();
            if (roleClaims.Count > 0)
            {
                claims.RemoveAll(c => c.Type == ClaimTypes.Role);
                foreach (var role in roleClaims)
                    claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }
        catch { /* Use default claims */ }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
