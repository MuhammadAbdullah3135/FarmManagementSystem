using System.Text;
using FMS.API.Middleware;
using FMS.Application.Animal;
using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Application.Configuration;
using FMS.Application.Attendance;
using FMS.Application.Performance;
using FMS.Application.Employees;
using FMS.Application.Farm;
using FMS.Application.Feed;
using FMS.Application.Tasks;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Auth;
using FMS.Infrastructure.Configuration;
using FMS.Infrastructure.Attendance;
using FMS.Infrastructure.Performance;
using FMS.Infrastructure.Employees;
using FMS.Infrastructure.Tasks;
using FMS.Infrastructure.Farm;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Files;
using FMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FMS API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: \"Bearer {token}\"",
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

// Database
builder.Services.AddDbContext<FmsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT Authentication
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

// Application Services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IFarmService, FarmService>();
builder.Services.AddScoped<IFarmContextService, FarmContext>();
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAnimalService, AnimalService>();
builder.Services.AddScoped<IFeedService, FeedService>();
    builder.Services.AddScoped<IEmployeeService, EmployeeService>();
    builder.Services.AddScoped<IFarmTaskService, FarmTaskService>();
    builder.Services.AddScoped<IAttendanceService, AttendanceService>();
    builder.Services.AddScoped<IPerformanceService, PerformanceService>();

// File Storage
var uploadsRoot = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Storage:UploadsRoot"] ?? "uploads");
builder.Services.AddSingleton<IFileStorageService>(new FileStorageService(uploadsRoot));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Seed roles
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
    await dbContext.Database.MigrateAsync();
    await RoleSeeder.SeedRolesAsync(dbContext);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("ReactApp");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<FarmContextMiddleware>();

// Serve uploaded files (images/documents) statically
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.MapControllers();

app.Run();
