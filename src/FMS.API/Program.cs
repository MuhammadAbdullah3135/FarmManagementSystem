using FluentValidation;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;


using System.Threading.RateLimiting;
using System.Text;
using FMS.API;
using FMS.API.Binding;
using FMS.API.Health;
using FMS.API.Jobs;
using FMS.API.Middleware;
using FMS.API.Security;
using FMS.Application.Animal;
using FMS.Application.Animal.Import;
using FMS.Application.Import;
using FMS.Application.Auth;
using FMS.Application.Breeding;
using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Application.Attendance;
using FMS.Application.Performance;
using FMS.Application.Employees;
using FMS.Application.Employees.Import;
using FMS.Application.Farm;
using FMS.Application.Feed;
using FMS.Application.Finance;
using FMS.Application.Health;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Application.Dashboard;
using FMS.Application.Email;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Application.Reports;
using FMS.Application.Tasks;
using FMS.Application.AuditLog;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Breeding;
using FMS.Infrastructure.Configuration;
using FMS.Infrastructure.Attendance;
using FMS.Infrastructure.Performance;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Tasks;
using FMS.Infrastructure.Farm;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Import;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Dashboard;
using FMS.Infrastructure.Email;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Notifications;
using FMS.Infrastructure.Reports;
using FMS.Infrastructure.AuditLog;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Before anything else: refuse to run outside Development on a signing key that
// cannot be trusted. See JwtSigningKeyGuard for why a committed placeholder key
// is worse than no authentication at all. Deliberately ahead of every service
// registration so this is the message an operator sees, not a downstream error.
JwtSigningKeyGuard.EnsureUsable(
    builder.Configuration["Jwt:SecretKey"],
    builder.Environment.EnvironmentName,
    builder.Environment.IsDevelopment());

// Email delivery is validated here too, for the same reason: a provider that is
// selected but unusable would accept every password reset and invitation and deliver
// none of them, and those two flows deliberately keep working when a send fails. The
// returned warning is logged once the host is built (see below).
var emailOptions = builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
    ?? new EmailOptions();
var emailConfigurationWarning = EmailConfigurationGuard.EnsureUsable(
    emailOptions,
    builder.Configuration,
    builder.Environment.EnvironmentName,
    builder.Environment.IsDevelopment());
builder.Host.UseSerilog();

// Dates are UTC by default: a value with no time zone ("2026-09-22") is read as UTC instead of
// Unspecified, which PostgreSQL rejects against every timestamp column in the schema. The rule
// is shared with the E2E test host through this extension so the two cannot drift apart.
builder.Services.AddControllers()
    .AddUtcDateHandling()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FMS API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddScoped<AuditLogInterceptor>();
builder.Services.AddDbContext<FmsDbContext>((sp, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));

var jwtKey = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});
builder.Services.AddAuthorization();

// FluentValidation - auto-validates all request DTOs
// FluentValidation auto-validates via [ApiController] model state
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Rate Limiting
// Rate Limiting
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 300,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.OnRejected = async (context, ct) => {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new {
            error = "Rate limit exceeded",
            retryAfter = 60
        }, ct);
    };
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!, name: "postgresql", tags: new[] { "ready" });
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
// Email transport: Log is the default and keeps the previous behaviour (messages are
// rendered and logged, not sent); SendGrid delivers them. The IEmailService contract
// and its registration below are unchanged either way.
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));

if (emailOptions.Provider == EmailProviderKind.SendGrid)
{
    builder.Services.AddHttpClient<SendGridEmailTransport>(client =>
    {
        client.BaseAddress = new Uri(emailOptions.SendGrid.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, emailOptions.RequestTimeoutSeconds));
    });

    builder.Services.AddScoped<IEmailTransport>(sp => sp.GetRequiredService<SendGridEmailTransport>());
}
else
{
    builder.Services.AddSingleton<IEmailTransport, LoggingEmailTransport>();
}

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFarmService, FarmService>();
builder.Services.AddScoped<IFarmMembershipService, FarmMembershipService>();
builder.Services.AddScoped<IFarmContextService, FarmContext>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<IConfigurationService>(sp =>
    new CachedConfigurationService(sp.GetRequiredService<ConfigurationService>(),
        sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAnimalService, AnimalService>();

// ── Bulk import: animals, employees, inventory ─────────────────────────
// CSV and .xlsx are parsed server-side behind ISpreadsheetReader, so every client
// (web, mobile, script) imports through one pipeline, and each row is validated with
// the rules the single-record create endpoint already uses. The limits live in the
// Import section; the pre-4.3 AnimalImport keys still apply when the new ones are
// absent, so a configured deployment cannot silently fall back to a default.
builder.Services.Configure<ImportOptions>(
    builder.Configuration.GetSection(ImportOptions.SectionName));
builder.Services.PostConfigure<ImportOptions>(options =>
    ImportOptionsBinding.ApplyLegacyFallback(options, builder.Configuration));
builder.Services.AddScoped<ISpreadsheetReader, SpreadsheetReader>();
builder.Services.AddScoped<IAnimalImportService, AnimalImportService>();
builder.Services.AddScoped<IInventoryImportService, InventoryImportService>();
builder.Services.AddScoped<IEmployeeImportService, EmployeeImportService>();
builder.Services.AddScoped<IBreedingService, BreedingService>();
builder.Services.AddScoped<IFeedService, FeedService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<IFarmTaskService, FarmTaskService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IPerformanceService, PerformanceService>();
builder.Services.AddScoped<IMedicalRecordService, MedicalRecordService>();
builder.Services.AddScoped<IMedicineService, MedicineService>();
builder.Services.AddScoped<IVaccineService, VaccineService>();
builder.Services.AddScoped<IWeightCheckScheduleService, WeightCheckScheduleService>();
builder.Services.AddScoped<IHealthCostService, HealthCostService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ICostAttributionService, CostAttributionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// ── Notifications ──────────────────────────────────────────────────────
// The recipient's own alert history: persisted notifications, per-user channel
// preferences, and the dispatch that keeps them in step with the dashboard's
// alert conditions. Email is the one out-of-app channel; push and SMS are not
// implemented, and the preference model already has a slot for them.
builder.Services.Configure<NotificationOptions>(
    builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddScoped<INotificationService, NotificationService>();

// ── Background jobs ────────────────────────────────────────────────────
// Hangfire schedules the recurring work (daily feeding-task generation, hourly
// health-status snapshots) against the same PostgreSQL database the app already
// uses, so no extra infrastructure is introduced. Job storage is configured
// here but created below, after EF migrations have run.
var jobOptions = BackgroundJobsSetup.ResolveOptions(builder.Configuration);
builder.Services.AddFmsBackgroundJobs(builder.Configuration, jobOptions);

// ── File storage ───────────────────────────────────────────────────────
// Local disk is the default so development and CI need no credentials. Any
// S3-compatible endpoint (Cloudflare R2 recommended, or AWS S3 / MinIO) is
// selected with Storage__Provider=S3 plus the Storage__S3__* settings.
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
    ?? new StorageOptions();

var usingLocalStorage = !storageOptions.IsS3;
if (storageOptions.IsS3)
{
    // Misconfiguration is refused outright rather than silently falling back to a
    // disk that disappears on restart.
    builder.Services.AddSingleton<IFileStorageService>(_ => S3FileStorageService.Create(storageOptions));
}
else
{
    var uploadsRoot = Path.Combine(builder.Environment.ContentRootPath, storageOptions.UploadsRoot);
    Directory.CreateDirectory(uploadsRoot);
    builder.Services.AddSingleton<IFileStorageService>(new FileStorageService(uploadsRoot));
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:3000" };
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

var app = builder.Build();

if (usingLocalStorage && !app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(
        "File storage is using local disk in the '{Environment}' environment. Container filesystems " +
        "(for example Heroku) are ephemeral, so uploaded files are lost on the next release or dyno cycle. " +
        "Set Storage__Provider=S3 to use object storage.",
        app.Environment.EnvironmentName);
}

if (emailConfigurationWarning is not null)
{
    app.Logger.LogWarning("Email configuration: {EmailWarning}", emailConfigurationWarning);
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
    await dbContext.Database.MigrateAsync();
    await RoleSeeder.SeedRolesAsync(dbContext);
}

if (jobOptions.Enabled)
{
    // Hangfire's own job-storage schema is installed here — deliberately AFTER
    // EF migrations above — and the recurring jobs are registered on every start.
    BackgroundJobsSetup.Initialize(app.Services, jobOptions, app.Logger);
}
else
{
    app.Logger.LogWarning(
        "Background jobs are disabled (Jobs:Enabled = false): no scheduled feed-task generation or health-status " +
        "recalculation will run in this process. The job status view reports this.");
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
if (!app.Environment.IsDevelopment()) { app.UseHsts(); }

app.UseHttpsRedirection();
app.UseCors("ReactApp");
app.UseAuthentication();

// Farm context must be resolved BEFORE authorization on farm-scoped routes.
// Their [Authorize(Roles = "…")] checks must enforce the caller's farm role
// (UserFarm.Role for the resolved farm), so FarmContextMiddleware swaps the
// principal's role claim — derived from the account JWT — before UseAuthorization
// evaluates it. Account-scoped routes (auth, farms list, invitations) are
// untouched: the middleware no-ops for those paths, so they keep account roles.
app.UseMiddleware<FarmContextMiddleware>();
app.UseAuthorization();

// The scheduler dashboard is a development tool only. A browser navigation to
// it cannot carry the Bearer token the SPA uses, so it is not mountable behind
// the app's normal auth in production; SystemOwners use /api/admin/jobs there
// instead. Local loopback requests are allowed in Development so the dashboard
// stays usable without weakening anything reachable off-box.
if (app.Environment.IsDevelopment() && jobOptions.Enabled)
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        DashboardTitle = "FMS scheduled jobs",
        Authorization = new[] { new SystemOwnerDashboardAuthorizationFilter(app.Environment) }
    });
}

// NOTE: uploads are deliberately NOT served as static files. A /uploads static path ran
// after the authorization middleware, so it published every animal image and document
// anonymously (no token, no farm scoping) behind nothing but an unguessable file name.
// Files are now reached only through authorised endpoints that either stream them or hand
// out a short-lived presigned URL.

var hcOptions = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        // Which commit is answering is part of this endpoint's job now: it is where the
        // post-deploy smoke check reads the build from, so a release that never actually landed
        // is a failed check rather than something someone notices by hand weeks later.
        await context.Response.WriteAsJsonAsync(HealthPayload.For(
            report,
            BuildStamp.Sha(builder.Configuration),
            app.Environment.EnvironmentName));
    }
};
app.MapHealthChecks("/health", hcOptions);

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
    }
});

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, _) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { status = "Healthy" });
    }
});

app.MapControllers();

var herokuPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(herokuPort))
{
    app.Urls.Clear();
    app.Urls.Add($"http://+:{herokuPort}");
}
app.Run();
