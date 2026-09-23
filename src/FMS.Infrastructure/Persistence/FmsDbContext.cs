using FMS.Domain.Common;
using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Persistence;

public class FmsDbContext : DbContext
{
    public FmsDbContext(DbContextOptions<FmsDbContext> options) : base(options) { }

    // Flag to prevent audit log interceptor recursion
    internal bool IsSavingAuditLogs { get; set; }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Domain.Entities.Farm> Farms => Set<Domain.Entities.Farm>();
    public DbSet<FarmConfiguration> FarmConfigurations => Set<FarmConfiguration>();
    public DbSet<UserFarm> UserFarms => Set<UserFarm>();
    public DbSet<FarmInvitation> FarmInvitations => Set<FarmInvitation>();
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
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<IncomeCategory> IncomeCategories => Set<IncomeCategory>();
    public DbSet<IncomeRecord> IncomeRecords => Set<IncomeRecord>();
    public DbSet<FarmTask> FarmTasks => Set<FarmTask>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<PerformanceReview> PerformanceReviews => Set<PerformanceReview>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<MedicineStock> MedicineStocks => Set<MedicineStock>();
    public DbSet<MedicineUsage> MedicineUsages => Set<MedicineUsage>();
    public DbSet<VaccineType> VaccineTypes => Set<VaccineType>();
    public DbSet<VaccinationRecord> VaccinationRecords => Set<VaccinationRecord>();
    public DbSet<VaccinationSchedule> VaccinationSchedules => Set<VaccinationSchedule>();
    public DbSet<WeightCheckSchedule> WeightCheckSchedules => Set<WeightCheckSchedule>();
    public DbSet<FarmHealthStatusSnapshot> FarmHealthStatusSnapshots => Set<FarmHealthStatusSnapshot>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<BreedingRecord> BreedingRecords => Set<BreedingRecord>();
    public DbSet<GestationRecord> GestationRecords => Set<GestationRecord>();
    public DbSet<GestationHealthCheck> GestationHealthChecks => Set<GestationHealthCheck>();
    public DbSet<BirthRecord> BirthRecords => Set<BirthRecord>();
    public DbSet<BirthOffspring> BirthOffspring => Set<BirthOffspring>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierPurchase> SupplierPurchases => Set<SupplierPurchase>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerSale> CustomerSales => Set<CustomerSale>();
    public DbSet<Domain.Entities.AuditLog> AuditLogs => Set<Domain.Entities.AuditLog>();

    /// <summary>The idempotency ledger for queued offline mutations (see <c>ProcessedMutation</c>).</summary>
    public DbSet<ProcessedMutation> ProcessedMutations => Set<ProcessedMutation>();

    // ── the modification stamp ─────────────────────────────
    //
    // Every row's timestamps are set here, on the way out, rather than by each service. That
    // is not tidiness: offline delta reads ask "what changed since <cursor>", so a row that
    // was written without a timestamp is a row a device will never learn about. Relying on
    // ~128 call sites to remember is how that happens, and the one write that matters most —
    // a soft delete, which is a Modified entry setting IsDeleted — is exactly the kind a
    // service can perform without touching ModifiedAt at all.
    //
    // Only the two overloads that do the work are overridden: DbContext's no-argument
    // SaveChanges/SaveChangesAsync delegate to these, so there is one stamp per save rather
    // than one per layer.

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Stamps the rows this save is writing.
    ///
    /// <para>
    /// Skipped while the audit interceptor is saving its own rows: that nested save is the one
    /// that actually flushes the business changes, and restamping there would move
    /// <c>ModifiedAt</c> a few microseconds past the value the audit entry just recorded — the
    /// audit payload and the row would disagree about the same write.
    /// </para>
    /// </summary>
    private void StampTimestamps()
    {
        if (IsSavingAuditLogs) return;

        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // A value set deliberately (an import, a seeder, a backfill) is never
                    // overwritten; only a row that arrived with no timestamp gets one.
                    if (entry.Entity.CreatedAt == default)
                        entry.Entity.CreatedAt = now;
                    break;

                case EntityState.Modified:
                    // Includes a soft delete: marking a row IsDeleted is a modification, so the
                    // tombstone carries a fresh timestamp whether the service set one or not.
                    entry.Entity.ModifiedAt = now;
                    break;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FmsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Applies the UTC convention to every date property in the model. Every date column in
    /// the schema is <c>timestamp with time zone</c>, and PostgreSQL rejects Unspecified
    /// values against those, so this is what keeps a date constructed anywhere in the server
    /// from failing at the database. See <see cref="UtcDateTimeConverter"/>.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();

        base.ConfigureConventions(configurationBuilder);
    }
}
