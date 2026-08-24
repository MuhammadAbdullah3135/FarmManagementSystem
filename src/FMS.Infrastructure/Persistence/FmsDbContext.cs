using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Persistence;

public class FmsDbContext : DbContext
{
    public FmsDbContext(DbContextOptions<FmsDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Domain.Entities.Farm> Farms => Set<Domain.Entities.Farm>();
    public DbSet<FarmConfiguration> FarmConfigurations => Set<FarmConfiguration>();
    public DbSet<UserFarm> UserFarms => Set<UserFarm>();
    public DbSet<AnimalType> AnimalTypes => Set<AnimalType>();
    public DbSet<Breed> Breeds => Set<Breed>();
    public DbSet<SexOption> SexOptions => Set<SexOption>();
    public DbSet<AgeCategory> AgeCategories => Set<AgeCategory>();
    public DbSet<AnimalStatus> AnimalStatuses => Set<AnimalStatus>();
    public DbSet<IdentificationType> IdentificationTypes => Set<IdentificationType>();
    public DbSet<LocationType> LocationTypes => Set<LocationType>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<AnimalIdentification> AnimalIdentifications => Set<AnimalIdentification>();
    public DbSet<WeightRecord> WeightRecords => Set<WeightRecord>();
    public DbSet<AnimalImage> AnimalImages => Set<AnimalImage>();
    public DbSet<AnimalDocument> AnimalDocuments => Set<AnimalDocument>();
    public DbSet<AnimalTimelineEvent> AnimalTimelineEvents => Set<AnimalTimelineEvent>();
    public DbSet<AnimalTransfer> AnimalTransfers => Set<AnimalTransfer>();
    public DbSet<FeedType> FeedTypes => Set<FeedType>();
    public DbSet<FeedStockMovement> FeedStockMovements => Set<FeedStockMovement>();
    public DbSet<FeedRecord> FeedRecords => Set<FeedRecord>();
    public DbSet<DietPlan> DietPlans => Set<DietPlan>();
    public DbSet<DietPlanItem> DietPlanItems => Set<DietPlanItem>();
    public DbSet<FeedingSchedule> FeedingSchedules => Set<FeedingSchedule>();
    public DbSet<FeedingTask> FeedingTasks => Set<FeedingTask>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<EmployeeRole> EmployeeRoles => Set<EmployeeRole>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<SalaryPayment> SalaryPayments => Set<SalaryPayment>();
    public DbSet<FarmTask> FarmTasks => Set<FarmTask>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FmsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
