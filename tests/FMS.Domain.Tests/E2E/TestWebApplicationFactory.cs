using System.Security.Claims;
using System.Text;
using FMS.API.Binding;
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

                    // Farm context runs BETWEEN authentication and authorization,
                    // mirroring Program.cs, so farm-scoped role checks are enforced
                    // from the resolved farm's role rather than the account role.
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

                    app.UseAuthorization();
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

                    // Add controllers from the API assembly. The date-handling rule is applied
                    // through the same extension Program.cs uses: this host builds its own MVC
                    // pipeline, so a registration made only in Program.cs would be exercised by
                    // no test, and that drift is how date-only query values reached PostgreSQL
                    // as Unspecified (a 400 in production) with the suite still green.
                    services.AddControllers()
                        .AddApplicationPart(typeof(FMS.API.Controllers.DashboardController).Assembly)
                        .AddUtcDateHandling()
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
                    services.AddScoped<FMS.Application.Inventory.ISupplierService, FMS.Infrastructure.Inventory.SupplierService>();
                    services.AddScoped<FMS.Application.Inventory.ICustomerService, FMS.Infrastructure.Inventory.CustomerService>();
                    services.AddScoped<FMS.Application.Feed.IFeedService, FMS.Infrastructure.Feed.FeedService>();
                    services.AddScoped<FMS.Application.Finance.IFinanceService, FMS.Infrastructure.Finance.FinanceService>();
                    services.AddScoped<FMS.Application.Health.IVaccineService, FMS.Infrastructure.Health.VaccineService>();
                    services.AddScoped<FMS.Application.Health.IMedicineService, FMS.Infrastructure.Health.MedicineService>();
                    services.AddScoped<FMS.Application.Health.IMedicalRecordService, FMS.Infrastructure.Health.MedicalRecordService>();
                    services.AddScoped<FMS.Application.Health.IHealthCostService, FMS.Infrastructure.Health.HealthCostService>();
                    services.AddScoped<FMS.Application.Health.IWeightCheckScheduleService, FMS.Infrastructure.Health.WeightCheckScheduleService>();
                    services.AddScoped<FMS.Application.Tasks.IFarmTaskService, FMS.Infrastructure.Tasks.FarmTaskService>();
                    services.AddScoped<FMS.Application.Attendance.IAttendanceService, FMS.Infrastructure.Attendance.AttendanceService>();
                    // Queued offline mutations (phase 5.3). Registered here for the same reason the
                    // import services are: this host builds its own graph, so a registration made
                    // only in Program.cs would be exercised by no test.
                    services.AddScoped<FMS.Application.Sync.IMutationSyncService, FMS.Infrastructure.Sync.MutationSyncService>();
                    services.AddScoped<FMS.Application.Animal.IAnimalService, FMS.Infrastructure.Animals.AnimalService>();
                    services.AddScoped<FMS.Application.Breeding.IBreedingService, FMS.Infrastructure.Breeding.BreedingService>();
                    services.AddScoped<FMS.Application.Employees.IEmployeeService, FMS.Infrastructure.Employees.EmployeeService>();
                    services.AddScoped<FMS.Application.Dashboard.IDashboardService, FMS.Infrastructure.Dashboard.DashboardService>();
                    services.AddScoped<FMS.Application.Reports.IReportService, FMS.Infrastructure.Reports.ReportService>();
                    services.AddScoped<FMS.Application.Reports.ICostAttributionService, FMS.Infrastructure.Reports.CostAttributionService>();
                    services.AddScoped<FMS.Application.AuditLog.IAuditLogService, FMS.Infrastructure.AuditLog.AuditLogService>();
                    // Configuration lookups (same registration shape as Program.cs).
                    services.AddMemoryCache();
                    services.AddScoped<FMS.Infrastructure.Configuration.ConfigurationService>();
                    services.AddScoped<FMS.Application.Configuration.IConfigurationService>(sp =>
                        new FMS.Infrastructure.Configuration.CachedConfigurationService(
                            sp.GetRequiredService<FMS.Infrastructure.Configuration.ConfigurationService>(),
                            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
                    services.AddScoped<FMS.Application.Farm.IFarmService, FMS.Infrastructure.Farm.FarmService>();
                    services.AddScoped<FMS.Application.Farm.IFarmMembershipService, FMS.Infrastructure.Farm.FarmMembershipService>();
                    services.AddScoped<FMS.Application.Auth.IAuthService, FMS.Infrastructure.Auth.AuthService>();
                    services.AddScoped<FMS.Application.Auth.IJwtTokenService, FMS.Infrastructure.Auth.JwtTokenService>();
                    // Singleton so all request scopes share the same capturing instance.
                    services.AddSingleton<FMS.Application.Auth.IEmailService, PasswordResetE2ETests.TestEmailService>();

                    // Background jobs. The test host runs no Hangfire scheduler (its
                    // storage is PostgreSQL, which InMemory cannot stand in for), so the
                    // status provider is faked and the job classes are registered so
                    // tests can invoke their entry points directly — the same methods
                    // Hangfire would call.
                    services.AddLogging();
                    services.Configure<FMS.Application.Jobs.JobOptions>(_ => { });
                    services.AddScoped<FMS.Application.Jobs.IFarmDirectory, FMS.Infrastructure.Jobs.FarmDirectory>();
                    services.AddScoped<FMS.Infrastructure.Jobs.FeedingTaskGenerationJob>();
                    services.AddScoped<FMS.Infrastructure.Jobs.HealthStatusRecalculationJob>();
                    services.AddSingleton<FMS.Application.Jobs.IJobStatusProvider, Jobs.FakeJobStatusProvider>();

                    // Full-farm export (phase 6.2). The request path needs a job client to
                    // enqueue with, and the test host runs no Hangfire server, so the
                    // recording stand-in is registered as the container's implementation:
                    // tests assert what was enqueued, then invoke the job directly — the
                    // same entry point Hangfire would call.
                    services.AddSingleton<Hangfire.IBackgroundJobClient, Jobs.RecordingBackgroundJobClient>();
                    services.Configure<FMS.Application.Farm.Export.FarmExportOptions>(_ => { });
                    services.AddScoped<FMS.Infrastructure.Farm.Export.FarmExportAssembler>();
                    services.AddScoped<FMS.Application.Farm.Export.IFarmExportQueue,
                        FMS.API.Jobs.HangfireFarmExportQueue>();
                    services.AddScoped<FMS.Application.Farm.Export.IFarmExportService,
                        FMS.Infrastructure.Farm.Export.FarmExportService>();
                    services.AddScoped<FMS.Infrastructure.Jobs.FarmExportJob>();

                    // Notifications. The dispatcher is a plain DI-resolvable class
                    // (Hangfire activates it the same way), so tests can invoke it
                    // directly and then exercise the HTTP surface over the result.
                    services.Configure<FMS.Application.Notifications.NotificationOptions>(_ => { });
                    services.AddScoped<FMS.Application.Notifications.INotificationService, FMS.Infrastructure.Notifications.NotificationService>();
                    services.AddScoped<FMS.Application.Notifications.INotificationDispatcher, FMS.Infrastructure.Notifications.NotificationDispatcher>();
                    services.AddScoped<FMS.Infrastructure.Jobs.NotificationDispatchJob>();

                    // Bulk animal import. The real reader and pipeline run against the
                    // test host's InMemory database, so the endpoint exercises the same
                    // code the API wires up.
                    //
                    // The create-animal validator is registered by name rather than by
                    // scanning the assembly: the test host has never enabled FluentValidation's
                    // MVC integration, and scanning here would silently start validating
                    // every existing E2E request body.
                    services.Configure<FMS.Application.Import.ImportOptions>(_ => { });
                    services.AddScoped<FMS.Application.Import.ISpreadsheetReader,
                        FMS.Infrastructure.Import.SpreadsheetReader>();
                    services.AddScoped<FMS.Application.Animal.Import.IAnimalImportService,
                        FMS.Infrastructure.Import.AnimalImportService>();
                    services.AddScoped<FMS.Application.Inventory.Import.IInventoryImportService,
                        FMS.Infrastructure.Import.InventoryImportService>();
                    services.AddScoped<FMS.Application.Employees.Import.IEmployeeImportService,
                        FMS.Infrastructure.Import.EmployeeImportService>();
                    services.AddScoped<FMS.Application.Inventory.Import.ISupplierImportService,
                        FMS.Infrastructure.Import.SupplierImportService>();
                    services.AddScoped<FMS.Application.Inventory.Import.ICustomerImportService,
                        FMS.Infrastructure.Import.CustomerImportService>();
                    services.AddScoped<FMS.Application.Finance.Import.IExpenseImportService,
                        FMS.Infrastructure.Import.ExpenseImportService>();
                    services.AddScoped<FMS.Application.Finance.Import.IIncomeImportService,
                        FMS.Infrastructure.Import.IncomeImportService>();
                    services.AddScoped<FluentValidation.IValidator<FMS.Application.Animal.CreateAnimalRequest>,
                        FMS.API.Validation.Animals.CreateAnimalRequestValidator>();

                    // AnimalService takes file storage for animal images and documents;
                    // the import pipeline injects that service, so the graph needs it even
                    // though nothing here uploads a file.
                    services.AddSingleton<FMS.Application.Common.IFileStorageService>(
                        new FMS.Infrastructure.Files.FileStorageService(
                            Path.Combine(Path.GetTempPath(), "fms-e2e-uploads")));

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
