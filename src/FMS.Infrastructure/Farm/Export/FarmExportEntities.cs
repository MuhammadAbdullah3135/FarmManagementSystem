using FMS.Application.Animal.Import;
using FMS.Application.Employees.Import;
using FMS.Application.Farm.Export;
using FMS.Application.Finance.Import;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Farm.Export;

/// <summary>
/// What a full-farm export contains, in one place.
///
/// <para>
/// The list is explicit rather than discovered, because the columns of the seven
/// entities that have an importer are that importer's own vocabulary — a discovered
/// schema could not know that an animal's sex column is called "Sex" and resolves by
/// name. What <em>is</em> machine-checked is completeness: every entity in the model
/// that carries a <c>FarmId</c> must appear either here or in <see cref="Exclusions"/>,
/// asserted by <c>FarmExportEntityCoverageTests</c>. "The whole farm's data" is
/// therefore a claim the suite enforces, not a promise in a comment.
/// </para>
///
/// <para>
/// The seven importable entities come first, in the order the API documentation lists
/// them; everything else follows alphabetically by entity name. That order is the
/// archive's file order, so a person opening the ZIP meets the re-importable files
/// first.
/// </para>
/// </summary>
public static class FarmExportEntities
{
    /// <summary>
    /// The seven entities that also have a bulk import, and the file each one's data
    /// re-imports from.
    /// </summary>
    public static readonly IReadOnlyList<IFarmExportTable> Reimportable =
    [
        FarmExportTable<Animal>.ForImport(
            entity: "Animals",
            fileName: "animals.csv",
            catalog: AnimalImportFields.FieldCatalog,
            query: (db, farmId) => db.Animals
                .AsNoTracking()
                .Where(animal => animal.FarmId == farmId)
                .Include(animal => animal.AnimalType)
                .Include(animal => animal.Breed)
                .Include(animal => animal.SexOption)
                .Include(animal => animal.AgeCategory)
                .Include(animal => animal.AnimalStatus)
                .Include(animal => animal.Location)
                .Include(animal => animal.Sire)
                .Include(animal => animal.Dam)
                .OrderBy(animal => animal.TagNumber),
            values: new Dictionary<string, Func<Animal, string>>
            {
                [AnimalImportFields.TagNumber] = animal => animal.TagNumber,
                [AnimalImportFields.Name] = animal => animal.Name ?? string.Empty,
                [AnimalImportFields.AnimalType] = animal => animal.AnimalType.Name,
                [AnimalImportFields.Breed] = animal => animal.Breed?.Name ?? string.Empty,
                [AnimalImportFields.Sex] = animal => animal.SexOption.Value,
                [AnimalImportFields.AgeCategory] = animal => animal.AgeCategory?.Name ?? string.Empty,
                [AnimalImportFields.Status] = animal => animal.AnimalStatus.Name,
                [AnimalImportFields.Location] = animal => animal.Location?.Name ?? string.Empty,
                [AnimalImportFields.DateOfBirth] = animal => FarmExportValue.FormatDate(animal.DateOfBirth),
                [AnimalImportFields.AcquisitionDate] = animal => FarmExportValue.FormatDate(animal.AcquisitionDate),
                [AnimalImportFields.SireTag] = animal => animal.Sire?.TagNumber ?? string.Empty,
                [AnimalImportFields.DamTag] = animal => animal.Dam?.TagNumber ?? string.Empty,
                [AnimalImportFields.Notes] = animal => animal.Notes ?? string.Empty,
            }),

        FarmExportTable<Employee>.ForImport(
            entity: "Employees",
            fileName: "employees.csv",
            catalog: EmployeeImportFields.FieldCatalog,
            query: (db, farmId) => db.Employees
                .AsNoTracking()
                .Where(employee => employee.FarmId == farmId)
                .Include(employee => employee.Department)
                .Include(employee => employee.EmployeeRole)
                .OrderBy(employee => employee.LastName)
                .ThenBy(employee => employee.FirstName),
            values: new Dictionary<string, Func<Employee, string>>
            {
                [EmployeeImportFields.FirstName] = employee => employee.FirstName,
                [EmployeeImportFields.LastName] = employee => employee.LastName,
                [EmployeeImportFields.Email] = employee => employee.Email ?? string.Empty,
                [EmployeeImportFields.Phone] = employee => employee.Phone ?? string.Empty,
                [EmployeeImportFields.Address] = employee => employee.Address ?? string.Empty,
                [EmployeeImportFields.Department] = employee => employee.Department?.Name ?? string.Empty,
                [EmployeeImportFields.Role] = employee => employee.EmployeeRole?.Name ?? string.Empty,
                [EmployeeImportFields.SalaryType] = employee => employee.SalaryType.ToString(),
                [EmployeeImportFields.SalaryRate] = employee => FarmExportValue.Format(employee.SalaryRate),
                [EmployeeImportFields.HireDate] = employee => FarmExportValue.FormatDate(employee.HireDate),
                [EmployeeImportFields.Notes] = employee => employee.Notes ?? string.Empty,
            }),

        FarmExportTable<InventoryItem>.ForImport(
            entity: "InventoryItems",
            fileName: "inventory-items.csv",
            catalog: InventoryImportFields.FieldCatalog,
            query: (db, farmId) => db.InventoryItems
                .AsNoTracking()
                .Where(item => item.FarmId == farmId)
                .OrderBy(item => item.Name),
            values: new Dictionary<string, Func<InventoryItem, string>>
            {
                [InventoryImportFields.Name] = item => item.Name,
                [InventoryImportFields.Category] = item => item.Category ?? string.Empty,
                [InventoryImportFields.Unit] = item => item.Unit,
                [InventoryImportFields.Quantity] = item => FarmExportValue.Format(item.Quantity),
                [InventoryImportFields.ReorderLevel] = item => FarmExportValue.Format(item.ReorderLevel),
                [InventoryImportFields.UnitCost] = item => FarmExportValue.Format(item.UnitCost),
                [InventoryImportFields.Location] = item => item.Location ?? string.Empty,
            }),

        FarmExportTable<Supplier>.ForImport(
            entity: "Suppliers",
            fileName: "suppliers.csv",
            catalog: SupplierImportFields.FieldCatalog,
            query: (db, farmId) => db.Suppliers
                .AsNoTracking()
                .Where(supplier => supplier.FarmId == farmId)
                .OrderBy(supplier => supplier.Name),
            values: new Dictionary<string, Func<Supplier, string>>
            {
                [SupplierImportFields.Name] = supplier => supplier.Name,
                [SupplierImportFields.ContactInfo] = supplier => supplier.ContactInfo ?? string.Empty,
                [SupplierImportFields.ProductsSupplied] = supplier => supplier.ProductsSupplied ?? string.Empty,
            }),

        FarmExportTable<Customer>.ForImport(
            entity: "Customers",
            fileName: "customers.csv",
            catalog: CustomerImportFields.FieldCatalog,
            query: (db, farmId) => db.Customers
                .AsNoTracking()
                .Where(customer => customer.FarmId == farmId)
                .OrderBy(customer => customer.Name),
            values: new Dictionary<string, Func<Customer, string>>
            {
                [CustomerImportFields.Name] = customer => customer.Name,
                [CustomerImportFields.ContactInfo] = customer => customer.ContactInfo ?? string.Empty,
            }),

        FarmExportTable<Expense>.ForImport(
            entity: "Expenses",
            fileName: "expenses.csv",
            catalog: ExpenseImportFields.FieldCatalog,
            query: (db, farmId) => db.Expenses
                .AsNoTracking()
                .Where(expense => expense.FarmId == farmId)
                .Include(expense => expense.Category)
                .Include(expense => expense.PaymentMethod)
                .Include(expense => expense.Animal)
                .Include(expense => expense.Location)
                .OrderBy(expense => expense.ExpenseDate)
                .ThenBy(expense => expense.Id),
            values: new Dictionary<string, Func<Expense, string>>
            {
                [ExpenseImportFields.ExpenseDate] = expense => FarmExportValue.FormatDate(expense.ExpenseDate),
                [ExpenseImportFields.Amount] = expense => FarmExportValue.Format(expense.Amount),
                [ExpenseImportFields.ExpenseCategory] = expense => expense.Category.Name,
                [ExpenseImportFields.PaymentMethod] = expense => expense.PaymentMethod.Name,
                [ExpenseImportFields.AnimalTag] = expense => expense.Animal?.TagNumber ?? string.Empty,
                [ExpenseImportFields.Location] = expense => expense.Location?.Name ?? string.Empty,
                [ExpenseImportFields.Description] = expense => expense.Description ?? string.Empty,
            }),

        FarmExportTable<IncomeRecord>.ForImport(
            entity: "IncomeRecords",
            fileName: "income-records.csv",
            catalog: IncomeImportFields.FieldCatalog,
            query: (db, farmId) => db.IncomeRecords
                .AsNoTracking()
                .Where(income => income.FarmId == farmId)
                .Include(income => income.Category)
                .Include(income => income.PaymentMethod)
                .Include(income => income.Animal)
                .Include(income => income.Location)
                .OrderBy(income => income.IncomeDate)
                .ThenBy(income => income.Id),
            values: new Dictionary<string, Func<IncomeRecord, string>>
            {
                [IncomeImportFields.IncomeDate] = income => FarmExportValue.FormatDate(income.IncomeDate),
                [IncomeImportFields.Amount] = income => FarmExportValue.Format(income.Amount),
                [IncomeImportFields.IncomeCategory] = income => income.Category.Name,
                [IncomeImportFields.PaymentMethod] = income => income.PaymentMethod.Name,
                [IncomeImportFields.AnimalTag] = income => income.Animal?.TagNumber ?? string.Empty,
                [IncomeImportFields.Location] = income => income.Location?.Name ?? string.Empty,
                [IncomeImportFields.Description] = income => income.Description ?? string.Empty,
            }),
    ];

    /// <summary>
    /// Every other farm-scoped record: the lookups that give the records above their
    /// meaning, and the transactional tables no importer covers.
    /// </summary>
    public static readonly IReadOnlyList<IFarmExportTable> Records =
    [
        FarmExportTable<AgeCategory>.ReflectedForFarm("AgeCategories", "age-categories.csv"),
        FarmExportTable<AnimalDocument>.ReflectedForFarm("AnimalDocuments", "animal-documents.csv"),
        FarmExportTable<AnimalIdentification>.ReflectedForFarm("AnimalIdentifications", "animal-identifications.csv"),
        FarmExportTable<AnimalImage>.ReflectedForFarm("AnimalImages", "animal-images.csv"),
        FarmExportTable<AnimalStatus>.ReflectedForFarm("AnimalStatuses", "animal-statuses.csv"),
        FarmExportTable<AnimalTimelineEvent>.ReflectedForFarm("AnimalTimelineEvents", "animal-timeline-events.csv"),
        FarmExportTable<AnimalTransfer>.ReflectedForFarm("AnimalTransfers", "animal-transfers.csv"),
        FarmExportTable<AnimalType>.ReflectedForFarm("AnimalTypes", "animal-types.csv"),
        FarmExportTable<AttendanceRecord>.ReflectedForFarm("AttendanceRecords", "attendance-records.csv"),

        // The audit trail is farm data and is included: it records what happened to the
        // records above, and is not reproducible from them.
        FarmExportTable<Domain.Entities.AuditLog>.ReflectedForFarm("AuditLogs", "audit-logs.csv"),

        FarmExportTable<BirthOffspring>.ReflectedForFarm("BirthOffspring", "birth-offspring.csv"),
        FarmExportTable<BirthRecord>.ReflectedForFarm("BirthRecords", "birth-records.csv"),
        FarmExportTable<BreedingRecord>.ReflectedForFarm("BreedingRecords", "breeding-records.csv"),

        // Breeds have no FarmId: they belong to an animal type, which does. Scoped
        // through that parent, because an unscoped query would put every farm's breeds
        // into one farm's archive.
        FarmExportTable<Breed>.Reflected(
            "Breeds",
            "breeds.csv",
            (db, farmId) => db.Breeds.AsNoTracking().Where(breed => breed.AnimalType.FarmId == farmId)),

        FarmExportTable<CustomFieldDefinition>.ReflectedForFarm("CustomFieldDefinitions", "custom-field-definitions.csv"),
        FarmExportTable<CustomerSale>.ReflectedForFarm("CustomerSales", "customer-sales.csv"),
        FarmExportTable<Department>.ReflectedForFarm("Departments", "departments.csv"),
        FarmExportTable<DietPlan>.ReflectedForFarm("DietPlans", "diet-plans.csv"),

        // Same reasoning as breeds: a diet-plan item is scoped through its plan.
        FarmExportTable<DietPlanItem>.Reflected(
            "DietPlanItems",
            "diet-plan-items.csv",
            (db, farmId) => db.DietPlanItems.AsNoTracking().Where(item => item.DietPlan.FarmId == farmId)),

        FarmExportTable<EmployeeRole>.ReflectedForFarm("EmployeeRoles", "employee-roles.csv"),
        FarmExportTable<ExpenseCategory>.ReflectedForFarm("ExpenseCategories", "expense-categories.csv"),
        FarmExportTable<FarmConfiguration>.ReflectedForFarm("FarmConfigurations", "farm-configurations.csv"),
        FarmExportTable<FarmInvitation>.ReflectedForFarm("FarmInvitations", "farm-invitations.csv"),
        FarmExportTable<FarmTask>.ReflectedForFarm("FarmTasks", "farm-tasks.csv"),
        FarmExportTable<FeedRecord>.ReflectedForFarm("FeedRecords", "feed-records.csv"),
        FarmExportTable<FeedStockMovement>.ReflectedForFarm("FeedStockMovements", "feed-stock-movements.csv"),
        FarmExportTable<FeedType>.ReflectedForFarm("FeedTypes", "feed-types.csv"),
        FarmExportTable<FeedingSchedule>.ReflectedForFarm("FeedingSchedules", "feeding-schedules.csv"),
        FarmExportTable<FeedingTask>.ReflectedForFarm("FeedingTasks", "feeding-tasks.csv"),
        FarmExportTable<GestationHealthCheck>.ReflectedForFarm("GestationHealthChecks", "gestation-health-checks.csv"),
        FarmExportTable<GestationRecord>.ReflectedForFarm("GestationRecords", "gestation-records.csv"),
        FarmExportTable<IdentificationType>.ReflectedForFarm("IdentificationTypes", "identification-types.csv"),
        FarmExportTable<IncomeCategory>.ReflectedForFarm("IncomeCategories", "income-categories.csv"),
        FarmExportTable<Location>.ReflectedForFarm("Locations", "locations.csv"),
        FarmExportTable<LocationType>.ReflectedForFarm("LocationTypes", "location-types.csv"),
        FarmExportTable<MedicalRecord>.ReflectedForFarm("MedicalRecords", "medical-records.csv"),
        FarmExportTable<Medicine>.ReflectedForFarm("Medicines", "medicines.csv"),
        FarmExportTable<MedicineStock>.ReflectedForFarm("MedicineStocks", "medicine-stocks.csv"),
        FarmExportTable<MedicineUsage>.ReflectedForFarm("MedicineUsages", "medicine-usages.csv"),
        FarmExportTable<Notification>.ReflectedForFarm("Notifications", "notifications.csv"),
        FarmExportTable<NotificationPreference>.ReflectedForFarm("NotificationPreferences", "notification-preferences.csv"),
        FarmExportTable<PaymentMethod>.ReflectedForFarm("PaymentMethods", "payment-methods.csv"),
        FarmExportTable<PerformanceReview>.ReflectedForFarm("PerformanceReviews", "performance-reviews.csv"),
        FarmExportTable<SalaryPayment>.ReflectedForFarm("SalaryPayments", "salary-payments.csv"),
        FarmExportTable<SexOption>.ReflectedForFarm("SexOptions", "sex-options.csv"),
        FarmExportTable<StockMovement>.ReflectedForFarm("StockMovements", "stock-movements.csv"),
        FarmExportTable<SupplierPurchase>.ReflectedForFarm("SupplierPurchases", "supplier-purchases.csv"),
        FarmExportTable<UserFarm>.ReflectedForFarm("UserFarms", "user-farms.csv"),
        FarmExportTable<VaccinationRecord>.ReflectedForFarm("VaccinationRecords", "vaccination-records.csv"),
        FarmExportTable<VaccinationSchedule>.ReflectedForFarm("VaccinationSchedules", "vaccination-schedules.csv"),
        FarmExportTable<VaccineType>.ReflectedForFarm("VaccineTypes", "vaccine-types.csv"),
        FarmExportTable<WeightCheckSchedule>.ReflectedForFarm("WeightCheckSchedules", "weight-check-schedules.csv"),
        FarmExportTable<WeightRecord>.ReflectedForFarm("WeightRecords", "weight-records.csv"),
    ];

    /// <summary>The archive's files, in the order they are written.</summary>
    public static IReadOnlyList<IFarmExportTable> Tables { get; } = [.. Reimportable, .. Records];

    /// <summary>
    /// Farm-adjacent tables deliberately left out, each with the reason the archive
    /// states. Exclusions are listed rather than merely absent so the gap is disclosed
    /// in the manifest instead of being discovered by whoever needed the data.
    /// </summary>
    public static readonly IReadOnlyList<FarmExportExclusionDto> Exclusions =
    [
        new()
        {
            Entity = "FarmHealthStatusSnapshot",
            Reason = "Derived cache of the farm's due and overdue counts. Recomputed by the "
                + "health-status job from the records this archive already contains."
        },
        new()
        {
            Entity = "ProcessedMutation",
            Reason = "Offline-sync idempotency ledger: device keys and their outcomes, kept so a "
                + "queued mutation is applied once. Transport bookkeeping, not farm records."
        },
        new()
        {
            Entity = "FarmExport",
            Reason = "This archive's own request records, including internal storage keys."
        },
        new()
        {
            Entity = "Farm",
            Reason = "The farm's own record: its name and id are in manifest.json, which is what "
                + "identifies the archive."
        },
        new()
        {
            Entity = "Account",
            Reason = "The billing account a farm belongs to, which can own several farms."
        },
        new()
        {
            Entity = "User",
            Reason = "Account-level sign-ins. Members are visible through user-farms.csv, which is "
                + "farm membership rather than credentials."
        },
        new()
        {
            Entity = "UserRole",
            Reason = "Account-level roles, not farm roles. A member's role in this farm is in "
                + "user-farms.csv."
        },
        new()
        {
            Entity = "Role",
            Reason = "The system's fixed role list, seeded at startup and identical for every farm."
        },
        new()
        {
            Entity = "RefreshToken",
            Reason = "A session credential. Exporting it would put a reusable login token in a file "
                + "the user downloads and stores."
        },
        new()
        {
            Entity = "PasswordResetToken",
            Reason = "A password-reset credential. Exporting it would let anyone holding the archive "
                + "reset the account's password."
        },
    ];

    /// <summary>
    /// What a reader must know to avoid mistaking the archive for something it is not.
    /// These travel inside the archive as well as in the API response.
    /// </summary>
    public static readonly IReadOnlyList<string> Notes =
    [
        "Raw records only. Computed and aggregated views — dashboard metrics and every report, "
            + "including cost per animal — are not included, because they are reproducible from "
            + "the files here; the reverse is not true.",

        "Uploaded files are not included. Animal images and documents appear as their metadata "
            + "rows (animal-images.csv, animal-documents.csv) with their storage paths, but the "
            + "file bytes themselves are not in this archive.",

        "Soft-deleted rows are included, with their isDeleted, deletedAt and deletedBy values. "
            + "Nothing the farm holds is silently omitted.",

        "Expenses and income records have no duplicate rule on import: neither has an identifier, "
            + "and the API refuses to invent one. Re-importing expenses.csv or income-records.csv "
            + "into a farm that already holds those records will create them a second time.",

        "References between records are written by name (and by id where the record has one), so "
            + "the files are readable without the database. The ids in them are the ids from this "
            + "farm at the moment of the export."
    ];

    /// <summary>
    /// Entity types the archive accounts for: every table plus every exclusion.
    ///</summary>
    public static IReadOnlySet<string> AccountedForEntityNames { get; } =
        Tables.Select(table => table.EntityType.Name)
            .Concat(Exclusions.Select(exclusion => exclusion.Entity))
            .ToHashSet(StringComparer.Ordinal);
}
