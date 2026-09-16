using FluentValidation;


using System.Threading.RateLimiting;
using System.Text;
using FMS.API.Middleware;
using FMS.Application.Animal;
using FMS.Application.Auth;
using FMS.Application.Breeding;
using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Application.Attendance;
using FMS.Application.Performance;
using FMS.Application.Employees;
using FMS.Application.Farm;
using FMS.Application.Feed;
using FMS.Application.Finance;
using FMS.Application.Health;
using FMS.Application.Inventory;
using FMS.Application.Dashboard;
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
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Dashboard;
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
builder.Host.UseSerilog();

builder.Services.AddControllers().AddJsonOptions(o =>
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
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IFarmService, FarmService>();
builder.Services.AddScoped<IFarmContextService, FarmContext>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<IConfigurationService>(sp =>
    new CachedConfigurationService(sp.GetRequiredService<ConfigurationService>(),
        sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAnimalService, AnimalService>();
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
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

var uploadsRoot = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Storage:UploadsRoot"] ?? "uploads");
Directory.CreateDirectory(uploadsRoot);
builder.Services.AddSingleton<IFileStorageService>(new FileStorageService(uploadsRoot));

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

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
    await dbContext.Database.MigrateAsync();
    await RoleSeeder.SeedRolesAsync(dbContext);
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
if (!app.Environment.IsDevelopment()) { app.UseHsts(); }

app.UseHttpsRedirection();
app.UseCors("ReactApp");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<FarmContextMiddleware>();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

var hcOptions = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            }),
            duration = report.TotalDuration.TotalMilliseconds
        };
        await context.Response.WriteAsJsonAsync(result);
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
