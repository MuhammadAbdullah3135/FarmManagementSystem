using FMS.Application.Common;
using FMS.Application.Farm;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Farm;

/// <summary>
/// Seeds the shared demo farm with realistic sample data covering every major feature so each
/// screen has something meaningful to show on first demo login.
///
/// Idempotent by design: safe to run many times. Each module's seed method is a no-op when the
/// data it is responsible for is already present, and the whole reset path (<c>ResetDemoDataAsync</c>)
/// tears the demo farm back to seed state rather than degrading the seeder into a "clear everything
/// first" routine.
///
/// Dates are computed relative to <paramref name="now"/> so the seed stays believable after the
/// clock moves (overdue tasks, upcoming births, expiring medicine, recent weight checks, etc.).
/// Names are fictional and contain no real personal data.
///
/// Reset semantics: the demo user, account and farm keep stable ids across resets (so a visitor's
/// session token remains valid until expiry), while all farm-scoped data and the demo user's
/// membership/role/token rows are wiped and re-seeded. The wipe is provider-agnostic (entity
/// removal for both InMemory and relational providers) so the reset path is testable on the
/// InMemory provider used by the suite and correct on PostgreSQL.
/// </summary>
public static partial class DemoDataSeeder
{
    /// <summary>
    /// Resets the demo account/farm/users to seed state and re-seeds every module. Idempotent:
    /// calling it repeatedly yields the same end state. A no-op when no demo user exists.
    /// </summary>
    public static async Task ResetDemoDataAsync(FmsDbContext context, DemoOptions options, DateTime now)
    {
        var demoUser = await context.Users
            .Include(u => u.Account)
            .Include(u => u.UserFarms)
                .ThenInclude(uf => uf.Farm)
            .FirstOrDefaultAsync(u => u.IsDemo);
        if (demoUser == null)
            return; // nothing to reset (controller layer reports NotFound)

        var farmId = demoUser.UserFarms.FirstOrDefault(uf => !uf.Farm.IsDeleted && uf.Farm.IsActive)?.FarmId
            ?? throw new InvalidOperationException("Demo user has no active farm.");
        var accountId = demoUser.AccountId;

        // ── Wipe only the demo farm's data, never anyone else's. ──────────────────────
        // Every removed entity is scoped to farmId (or to the demo user id for user-scoped rows),
        // so even a catastrophic bug here cannot touch another account's rows.
        await WipeDemoFarmDataAsync(context, farmId);
        await WipeDemoUserRelatedRowsAsync(context, demoUser.Id);
        // Flush the wipe before re-seeding. The seeder's "find or create" lookups
        // (medicines, statuses, categories, ...) run against the change tracker as well as the
        // store, so a save deferred to the end lets a lookup resolve to a row that is already
        // marked Deleted — the reseed then hangs a fresh child (e.g. a MedicineStock) off a
        // principal that this same reset is about to delete, and SaveChanges refuses.
        await context.SaveChangesAsync();

        // Reset the demo user + account + farm scalars in place so ids stay stable across resets
        // (a visitor's session token stays valid until expiry) and there is no tracked-entity
        // key conflict from remove-then-re-add.
        demoUser.Email = options.DemoEmail;
        demoUser.PasswordHash = options.DemoPasswordHash;
        demoUser.FirstName = "Demo";
        demoUser.LastName = "Visitor";
        demoUser.IsActive = true;
        demoUser.IsDemo = true;
        demoUser.Locale = null;
        demoUser.ModifiedAt = now;

        var account = demoUser.Account; // navigated
        if (account == null)
        {
            account = new Account { Id = accountId, Name = "Demo Farm", CreatedAt = now, ModifiedAt = now };
            context.Accounts.Add(account);
        }
        else
        {
            account.Name = "Demo Farm";
            account.ModifiedAt = now;
        }

        var farm = await context.Farms.FirstOrDefaultAsync(f => f.Id == farmId);
        if (farm == null)
        {
            farm = new FMS.Domain.Entities.Farm
            {
                Id = farmId, AccountId = accountId, Name = "Sunrise Ranch",
                Description = "Shared demo farm — every screen has sample data. This farm is restored to this state on demo reset.",
                IsActive = true, CreatedAt = now, ModifiedAt = now
            };
            context.Farms.Add(farm);
        }
        else
        {
            farm.Name = "Sunrise Ranch";
            farm.Description = "Shared demo farm — every screen has sample data. This farm is restored to this state on demo reset.";
            farm.IsActive = true;
            farm.ModifiedAt = now;
        }

        // Ensure the demo user's account-role (UserRole) is SystemOwner so the demo session can
        // call admin-only endpoints such as POST /auth/demo-reset. The Role row itself is created
        // here if it does not already exist (e.g. the test host does not run RoleSeeder).
        var systemOwnerRole = await EnsureSystemOwnerRoleAsync(context, now);
        var existingUserRole = await context.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == demoUser.Id);
        if (existingUserRole == null)
        {
            context.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(), UserId = demoUser.Id, RoleId = systemOwnerRole.Id, CreatedAt = now
            });
        }

        // Ensure the demo user's farm membership points at the demo farm with SystemOwner role.
        var existingUserFarm = await context.UserFarms.FirstOrDefaultAsync(uf => uf.UserId == demoUser.Id && uf.FarmId == farmId);
        if (existingUserFarm == null)
        {
            context.UserFarms.Add(new UserFarm
            {
                Id = Guid.NewGuid(), UserId = demoUser.Id, FarmId = farmId,
                Role = FarmRoles.SystemOwner, CreatedAt = now
            });
        }
        else
        {
            existingUserFarm.Role = FarmRoles.SystemOwner;
        }

        // Re-seed the lookup defaults (idempotent per category) + the domain data. The ensure
        // helpers re-create anything the wipe removed, and are no-ops for anything still present.
        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(context, farmId);
        await SeedEverythingAsync(context, farmId, now);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds the demo farm the first time <c>DemoLoginAsync</c> is called and no demo user exists.
    /// Idempotent at the farm level: if the demo farm already has domain data, this is a no-op.
    /// </summary>
    public static async Task EnsureDemoDataExistsAsync(FmsDbContext context, DemoOptions options, DateTime now)
    {
        var demoUser = await context.Users.FirstOrDefaultAsync(u => u.IsDemo);
        if (demoUser != null)
            return; // already seeded

        var accountId = Guid.NewGuid();
        var farmId = Guid.NewGuid();

        var account = new Account
        {
            Id = accountId, Name = "Demo Farm", CreatedAt = now, ModifiedAt = now
        };
        context.Accounts.Add(account);

        var farm = new FMS.Domain.Entities.Farm
        {
            Id = farmId, AccountId = accountId, Name = "Sunrise Ranch",
            Description = "Shared demo farm — every screen has sample data. This farm is restored to this state on demo reset.",
            IsActive = true, CreatedAt = now, ModifiedAt = now
        };
        context.Farms.Add(farm);

        var user = new User
        {
            Id = Guid.NewGuid(), AccountId = accountId, Email = options.DemoEmail,
            PasswordHash = options.DemoPasswordHash, FirstName = "Demo", LastName = "Visitor",
            IsActive = true, IsDemo = true, CreatedAt = now, ModifiedAt = now
        };
        context.Users.Add(user);

        context.UserFarms.Add(new UserFarm
        {
            Id = Guid.NewGuid(), UserId = user.Id, FarmId = farmId,
            Role = FarmRoles.SystemOwner, CreatedAt = now
        });

        // The account role that POST /auth/demo-reset authorizes against. The reset path creates
        // this too; the lazy first-login path must as well, or the fresh demo token carries no
        // SystemOwner role and the demo's own reset returns 403 — and it cannot recover, because
        // reset is exactly what requires the role.
        var systemOwnerRole = await EnsureSystemOwnerRoleAsync(context, now);
        context.UserRoles.Add(new UserRole
        {
            Id = Guid.NewGuid(), UserId = user.Id, RoleId = systemOwnerRole.Id, CreatedAt = now
        });

        await FarmDefaultsSeeder.EnsureFarmDefaultsAsync(context, farmId);
        await SeedEverythingAsync(context, farmId, now);
        await context.SaveChangesAsync();
    }

    // ── wipe helpers (provider-agnostic: RemoveRange works on both InMemory and relational) ──

    private static async Task WipeDemoFarmDataAsync(FmsDbContext context, Guid farmId)
    {
        // Children first is not required for RemoveRange (EF Core orders deletes at SaveChanges
        // using FK metadata), but removing every farm-scoped set before a single SaveChanges keeps
        // the change tracker complete so FK Restrict cannot block a parent delete.
        await RemoveRangeAsync(context, context.AnimalDocuments.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.AnimalImages.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.AnimalIdentifications.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.AnimalTransfers.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.WeightRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Animals.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.BirthOffspring.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.BirthRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.GestationHealthChecks.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.GestationRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.BreedingRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FeedRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FeedingSchedules.Where(a => a.FarmId == farmId));
        // DietPlanItems have no FarmId; remove them via the demo farm's diet plans
        // (before the plans themselves are removed).
        {
            var dietPlanIds = await context.DietPlans
                .Where(dp => dp.FarmId == farmId)
                .Select(dp => dp.Id)
                .ToListAsync();
            if (dietPlanIds.Count > 0)
            {
                await RemoveRangeAsync(context, context.DietPlanItems
                    .Where(dpi => dietPlanIds.Contains(dpi.DietPlanId)));
            }
        }
        await RemoveRangeAsync(context, context.DietPlans.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FeedingTasks.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FarmTasks.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FeedStockMovements.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FeedTypes.Where(a => a.FarmId == farmId));
        // MedicineUsage is not exposed as a DbSet on FmsDbContext, so it is intentionally
        // not wiped here — no demo seed data writes to it, so nothing leaks across resets.
        await RemoveRangeAsync(context, context.MedicalRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.VaccinationRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.VaccinationSchedules.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.MedicineStocks.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Medicines.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.VaccineTypes.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.WeightCheckSchedules.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Expenses.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.IncomeRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.PaymentMethods.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.IncomeCategories.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.ExpenseCategories.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.CustomerSales.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Customers.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.SupplierPurchases.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Suppliers.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.InventoryItems.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.StockMovements.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.AttendanceRecords.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.PerformanceReviews.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.SalaryPayments.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Employees.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Departments.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.EmployeeRoles.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FarmConfigurations.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Locations.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.LocationTypes.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.CustomFieldDefinitions.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.AnimalStatuses.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.SexOptions.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.AgeCategories.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.IdentificationTypes.Where(a => a.FarmId == farmId));
        // Breeds have no FarmId; remove the breeds that belong to the demo farm's animal types
        // before the animal types themselves. EF Core also reorders at SaveChanges, but doing it
        // explicitly keeps the wipe correct on InMemory without raw SQL.
        {
            var animalTypeIds = await context.AnimalTypes
                .Where(at => at.FarmId == farmId)
                .Select(at => at.Id)
                .ToListAsync();
            if (animalTypeIds.Count > 0)
            {
                await RemoveRangeAsync(context, context.Breeds
                    .Where(b => animalTypeIds.Contains(b.AnimalTypeId)));
            }
        }
        await RemoveRangeAsync(context, context.AnimalTypes.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.FarmExports.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.Notifications.Where(a => a.FarmId == farmId));
        await RemoveRangeAsync(context, context.NotificationPreferences.Where(a => a.FarmId == farmId));
    }

    private static async Task WipeDemoUserRelatedRowsAsync(FmsDbContext context, Guid userId)
    {
        await RemoveRangeAsync(context, context.RefreshTokens.Where(r => r.UserId == userId));
        await RemoveRangeAsync(context, context.PasswordResetTokens.Where(r => r.UserId == userId));
        await RemoveRangeAsync(context, context.UserRoles.Where(r => r.UserId == userId));
        await RemoveRangeAsync(context, context.UserFarms.Where(r => r.UserId == userId));
        await RemoveRangeAsync(context, context.PushSubscriptions.Where(r => r.UserId == userId));
    }

    private static async Task RemoveRangeAsync<T>(FmsDbContext context, IEnumerable<T> entities) where T : class
    {
        var list = entities.ToList();
        if (list.Count == 0) return;
        context.Set<T>().RemoveRange(list);
    }

    // ── tiny helpers ──────────────────────────────────────────────────────────────────────

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();

    private static async Task<Role> EnsureSystemOwnerRoleAsync(FmsDbContext context, DateTime now)
    {
        var existing = await context.Roles.FirstOrDefaultAsync(r => r.Name == FarmRoles.SystemOwner);
        if (existing != null) return existing;
        var role = new Role { Id = Guid.NewGuid(), Name = FarmRoles.SystemOwner, CreatedAt = now };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        return role;
    }

    /// <summary>
    /// Returns the farm's status with the given name, creating it when the default set does not
    /// already provide it. The demo seed needs statuses (<c>Sick</c>) that the shared farm
    /// defaults intentionally do not ship, so it creates them for the demo farm only.
    /// </summary>
    private static async Task<AnimalStatus> EnsureAnimalStatusAsync(FmsDbContext context, Guid farmId, string name, DateTime now)
    {
        var existing = await context.AnimalStatuses.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Name == name);
        if (existing != null) return existing;
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(), FarmId = farmId, Name = name,
            IsActive = true, Category = AnimalStatusCategory.Active,
            IsSystemDefined = true, CreatedAt = now,
        };
        context.AnimalStatuses.Add(status);
        await context.SaveChangesAsync();
        return status;
    }

    private static async Task<Employee> EnsureEmployeeAsync(FmsDbContext context, Guid farmId, DateTime now,
        string fullName, string email,
        SalaryType salaryType, decimal salaryRate, DateTime hireDate)
    {
        var nameParts = fullName.Split(' ', 2);
        var firstName = nameParts[0];
        var lastName = nameParts.Length > 1 ? nameParts[1] : "";
        var existing = await context.Employees.FirstOrDefaultAsync(e => e.Email == email);
        if (existing != null)
            return existing;
        var employee = new Employee
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            FirstName = firstName, LastName = lastName,
            Email = email, Phone = email,
            SalaryType = salaryType, SalaryRate = salaryRate,
            HireDate = hireDate, IsActive = true,
            Notes = ""
        };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();
        return employee;
    }

    private static async Task<Department> EnsureDepartmentAsync(FmsDbContext context, Guid farmId, DateTime now, string name)
    {
        var existing = await context.Departments.FirstOrDefaultAsync(d => d.FarmId == farmId && d.Name == name);
        if (existing != null) return existing;
        var dept = new Department { Id = Guid.NewGuid(), FarmId = farmId, Name = name };
        context.Departments.Add(dept);
        await context.SaveChangesAsync();
        return dept;
    }

    private static async Task<EmployeeRole> EnsureEmployeeRoleAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string description)
    {
        var existing = await context.EmployeeRoles.FirstOrDefaultAsync(r => r.FarmId == farmId && r.Name == name);
        if (existing != null) return existing;
        var role = new EmployeeRole { Id = Guid.NewGuid(), FarmId = farmId, Name = name, Description = description };
        context.EmployeeRoles.Add(role);
        await context.SaveChangesAsync();
        return role;
    }

    private static async Task<ExpenseCategory> EnsureExpenseCategoryAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string description)
    {
        var existing = await context.ExpenseCategories.FirstOrDefaultAsync(c => c.FarmId == farmId && c.Name == name);
        if (existing != null) return existing;
        var cat = new ExpenseCategory { Id = Guid.NewGuid(), FarmId = farmId, Name = name, Description = description };
        context.ExpenseCategories.Add(cat);
        await context.SaveChangesAsync();
        return cat;
    }

    private static async Task<IncomeCategory> EnsureIncomeCategoryAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string description)
    {
        var existing = await context.IncomeCategories.FirstOrDefaultAsync(c => c.FarmId == farmId && c.Name == name);
        if (existing != null) return existing;
        var cat = new IncomeCategory { Id = Guid.NewGuid(), FarmId = farmId, Name = name, Description = description };
        context.IncomeCategories.Add(cat);
        await context.SaveChangesAsync();
        return cat;
    }

    private static async Task<PaymentMethod> EnsurePaymentMethodAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string description)
    {
        var existing = await context.PaymentMethods.FirstOrDefaultAsync(p => p.FarmId == farmId && p.Name == name);
        if (existing != null) return existing;
        var pm = new PaymentMethod { Id = Guid.NewGuid(), FarmId = farmId, Name = name, Description = description };
        context.PaymentMethods.Add(pm);
        await context.SaveChangesAsync();
        return pm;
    }

    private static async Task<(Location Location, LocationType LocationType)> EnsureLocationTreeAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        var lt = await context.LocationTypes.FirstOrDefaultAsync(t => t.FarmId == farmId && t.Name == "Barn")
            ?? throw new InvalidOperationException("Barn location type missing after defaults seed.");
        var existing = await context.Locations.FirstOrDefaultAsync(l => l.FarmId == farmId && l.Name == "Main Barn");
        if (existing != null) return (existing, lt);
        var loc = new Location
        {
            Id = Guid.NewGuid(), FarmId = farmId, Name = "Main Barn",
            LocationTypeId = lt.Id
        };
        context.Locations.Add(loc);
        await context.SaveChangesAsync();
        return (loc, lt);
    }

    private static async Task<Medicine> EnsureMedicineWithStockAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string unit, int lowStockThreshold, int expiringSoonDays,
        int quantity, decimal unitCost, string batchNumber, DateTime expiryDate, DateTime dateReceived)
    {
        var existing = await context.Medicines.FirstOrDefaultAsync(m => m.FarmId == farmId && m.Name == name);
        if (existing != null)
        {
            context.MedicineStocks.Add(new MedicineStock
            {
                Id = Guid.NewGuid(), FarmId = farmId, MedicineId = existing.Id,
                BatchNumber = batchNumber, Quantity = quantity, UnitCost = unitCost,
                ExpiryDate = expiryDate, DateReceived = dateReceived,
                Supplier = "Sunrise Feeds Co."
            });
            await context.SaveChangesAsync();
            return existing;
        }
        var medicine = new Medicine
        {
            Id = Guid.NewGuid(), FarmId = farmId, Name = name,
            Unit = unit, LowStockThreshold = lowStockThreshold,
            ExpiringSoonDays = expiringSoonDays
        };
        context.Medicines.Add(medicine);
        await context.SaveChangesAsync();
        context.MedicineStocks.Add(new MedicineStock
        {
            Id = Guid.NewGuid(), FarmId = farmId, MedicineId = medicine.Id,
            BatchNumber = batchNumber, Quantity = quantity, UnitCost = unitCost,
            ExpiryDate = expiryDate, DateReceived = dateReceived,
            Supplier = "Sunrise Feeds Co."
        });
        await context.SaveChangesAsync();
        return medicine;
    }

    private static async Task<VaccineType> EnsureVaccineTypeAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string defaultDosage, Guid linkedMedicineId)
    {
        var existing = await context.VaccineTypes.FirstOrDefaultAsync(v => v.FarmId == farmId && v.Name == name);
        if (existing != null) return existing;
        var vax = new VaccineType
        {
            Id = Guid.NewGuid(), FarmId = farmId, Name = name, DefaultDosage = defaultDosage,
            LinkedMedicineId = linkedMedicineId
        };
        context.VaccineTypes.Add(vax);
        await context.SaveChangesAsync();
        return vax;
    }

    private static async Task<FeedType> EnsureFeedTypeAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, FeedCategory category, FeedUnit unit, decimal costPerUnit, string notes)
    {
        var existing = await context.FeedTypes.FirstOrDefaultAsync(f => f.FarmId == farmId && f.Name == name);
        if (existing != null) return existing;
        var ft = new FeedType
        {
            Id = Guid.NewGuid(), FarmId = farmId, Name = name,
            Category = category, CostPerUnit = costPerUnit,
            Unit = unit, Notes = notes
        };
        context.FeedTypes.Add(ft);
        await context.SaveChangesAsync();
        return ft;
    }

    private static async Task EnsureFeedStockMovementAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid feedTypeId, StockMovementType movementType, decimal quantity, decimal unitCost,
        string? supplier, DateTime movementDate)
    {
        context.FeedStockMovements.Add(new FeedStockMovement
        {
            Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = feedTypeId,
            MovementType = movementType, Quantity = quantity,
            UnitCost = unitCost, TotalCost = quantity * unitCost,
            Supplier = supplier, MovementDate = movementDate,
            Notes = ""
        });
        await context.SaveChangesAsync();
    }

    private static async Task<DietPlan> EnsureDietPlanAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid animalTypeId, Guid? breedId, string name, string notes, (Guid FeedTypeId, decimal QuantityPerFeeding)[] items)
    {
        var dp = new DietPlan
        {
            Id = Guid.NewGuid(), FarmId = farmId, Name = name,
            AnimalTypeId = animalTypeId, BreedId = breedId,
            IsActive = true, Notes = notes
        };
        context.DietPlans.Add(dp);
        await context.SaveChangesAsync();
        foreach (var (feedTypeId, qty) in items)
        {
            context.DietPlanItems.Add(new DietPlanItem
            {
                Id = Guid.NewGuid(), DietPlanId = dp.Id,
                FeedTypeId = feedTypeId, QuantityPerFeeding = qty
            });
        }
        await context.SaveChangesAsync();
        return dp;
    }

    private static async Task<FeedingSchedule> EnsureFeedingScheduleAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid dietPlanId, TimeSpan timeOfDay, string label)
    {
        var schedule = new FeedingSchedule
        {
            Id = Guid.NewGuid(), FarmId = farmId, DietPlanId = dietPlanId,
            TimeOfDay = timeOfDay, Label = label,
            IsActive = true
        };
        context.FeedingSchedules.Add(schedule);
        await context.SaveChangesAsync();
        return schedule;
    }

    private static async Task<InventoryItem> EnsureInventoryItemAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string category, string unit, decimal unitCost, int quantity, int reorderLevel)
    {
        var existing = await context.InventoryItems.FirstOrDefaultAsync(i => i.FarmId == farmId && i.Name == name);
        if (existing != null) return existing;
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(), FarmId = farmId, Name = name,
            Category = category, Unit = unit, Quantity = quantity,
            ReorderLevel = reorderLevel, UnitCost = unitCost,
            Location = "Main Barn"
        };
        context.InventoryItems.Add(item);
        await context.SaveChangesAsync();
        return item;
    }

    private static async Task EnsureStockMovementAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid inventoryItemId, InventoryMovementType movementType, int quantity, decimal unitCost, string? supplier)
    {
        context.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(), FarmId = farmId, InventoryItemId = inventoryItemId,
            MovementType = movementType, Quantity = quantity,
            MovementDate = now, Reason = supplier == null ? null : $"Purchase from {supplier}"
        });
        await context.SaveChangesAsync();
    }

    private static async Task<Supplier> EnsureSupplierAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string contactInfo, string productsSupplied)
    {
        var existing = await context.Suppliers.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Name == name);
        if (existing != null) return existing;
        var s = new Supplier { Id = Guid.NewGuid(), FarmId = farmId, Name = name, ContactInfo = contactInfo, ProductsSupplied = productsSupplied };
        context.Suppliers.Add(s);
        await context.SaveChangesAsync();
        return s;
    }

    private static async Task<Customer> EnsureCustomerAsync(FmsDbContext context, Guid farmId, DateTime now,
        string name, string contactInfo)
    {
        var existing = await context.Customers.FirstOrDefaultAsync(c => c.FarmId == farmId && c.Name == name);
        if (existing != null) return existing;
        var c = new Customer { Id = Guid.NewGuid(), FarmId = farmId, Name = name, ContactInfo = contactInfo };
        context.Customers.Add(c);
        await context.SaveChangesAsync();
        return c;
    }

    private static async Task EnsureSupplierPurchaseAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid supplierId, Guid inventoryItemId, int quantity, decimal totalCost,
        string notes, DateTime purchaseDate)
    {
        context.SupplierPurchases.Add(new SupplierPurchase
        {
            Id = Guid.NewGuid(), FarmId = farmId, SupplierId = supplierId,
            InventoryItemId = inventoryItemId, Quantity = quantity,
            TotalCost = totalCost,
            PurchaseDate = purchaseDate, Notes = notes
        });
        await context.SaveChangesAsync();
    }

    private static async Task EnsureCustomerSaleAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid customerId, Guid inventoryItemId, int quantity, decimal totalAmount,
        string notes, DateTime saleDate)
    {
        context.CustomerSales.Add(new CustomerSale
        {
            Id = Guid.NewGuid(), FarmId = farmId, CustomerId = customerId,
            InventoryItemId = inventoryItemId, Quantity = quantity,
            TotalAmount = totalAmount,
            SaleDate = saleDate, Notes = notes
        });
        await context.SaveChangesAsync();
    }

    private static async Task EnsureAttendanceAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid employeeId, AttendanceStatus status, DateTime date)
    {
        var existing = await context.AttendanceRecords.FirstOrDefaultAsync(a =>
            a.FarmId == farmId && a.EmployeeId == employeeId && a.Date.Date == date.Date);
        if (existing != null) return;
        context.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = employeeId,
            Date = date.Date, Status = status, Notes = ""
        });
        await context.SaveChangesAsync();
    }
}
