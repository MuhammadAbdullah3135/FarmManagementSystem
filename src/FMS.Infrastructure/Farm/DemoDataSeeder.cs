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
public static class DemoDataSeeder
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

    // ── domain data seed ──────────────────────────────────────────────────────────────────

    private static async Task SeedEverythingAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        // ── Staff: a small, believable team ──────────────────────────────────────────────
        var vet = await EnsureEmployeeAsync(context, farmId, now,
            "Dr. Aisha Karim", "avet@sunriseranch.example",
            SalaryType.Monthly, 9_500m, now.AddYears(-3));
        var manager = await EnsureEmployeeAsync(context, farmId, now,
            "Marcus Bell", "mbell@sunriseranch.example",
            SalaryType.Monthly, 6_200m, now.AddYears(-4));
        var worker = await EnsureEmployeeAsync(context, farmId, now,
            "Liam O'Connor", "liam@sunriseranch.example",
            SalaryType.Monthly, 3_100m, now.AddMonths(-1));

        // ── Departments & roles ──────────────────────────────────────────────────────────
        await EnsureDepartmentAsync(context, farmId, now, "Veterinary");
        await EnsureDepartmentAsync(context, farmId, now, "Operations");
        await EnsureEmployeeRoleAsync(context, farmId, now, "Veterinarian", "On-farm vet");
        await EnsureEmployeeRoleAsync(context, farmId, now, "Herdsman", "Animal handling and feeding");

        // ── Finance setup: categories + payment methods ──────────────────────────────────
        var feedCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Feed", "Feed and foraging costs");
        var healthCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Veterinary", "Medical and vaccine costs");
        var maintCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Maintenance", "Repairs and upkeep");
        var milkCat = await EnsureIncomeCategoryAsync(context, farmId, now, "Milk Sales", "Fresh milk sales");
        var livestockCat = await EnsureIncomeCategoryAsync(context, farmId, now, "Livestock Sales", "Animal sales");
        var cash = await EnsurePaymentMethodAsync(context, farmId, now, "Cash", "Cash payments");
        var bank = await EnsurePaymentMethodAsync(context, farmId, now, "Bank Transfer", "Bank transfers");

        // ── Animals: a curated herd with every status ─────────────────────────────────────
        var (location, shedType) = await EnsureLocationTreeAsync(context, farmId, now);
        var cattleType = await context.AnimalTypes.FirstOrDefaultAsync(t => t.FarmId == farmId && t.Name == "Cattle")
            ?? throw new InvalidOperationException("Cattle animal type missing after defaults seed.");
        var breed = await context.Breeds.FirstOrDefaultAsync(b => b.AnimalTypeId == cattleType.Id && b.Name == "Sahiwal")
            ?? throw new InvalidOperationException("Sahiwal breed missing after defaults seed.");
        var male = await context.SexOptions.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Value == "Male")
            ?? throw new InvalidOperationException("Male sex option missing after defaults seed.");
        var female = await context.SexOptions.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Value == "Female")
            ?? throw new InvalidOperationException("Female sex option missing after defaults seed.");
        var activeStatus = await context.AnimalStatuses.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Name == "Active")
            ?? throw new InvalidOperationException("Active status missing after defaults seed.");
        var pregnantStatus = await context.AnimalStatuses.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Name == "Pregnant")
            ?? throw new InvalidOperationException("Pregnant status missing after defaults seed.");
        var dryStatus = await context.AnimalStatuses.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Name == "Dry")
            ?? throw new InvalidOperationException("Dry status missing after defaults seed.");
        // The demo herd needs a "Sick" status to place its two ailing animals, but the shared
        // farm defaults deliberately ship only Active/Pregnant/Lactating/Dry/Sold/Deceased.
        // Ensure it here rather than widening every farm's defaults for a demo-only need.
        var sickStatus = await EnsureAnimalStatusAsync(context, farmId, "Sick", now);
        var soldStatus = await context.AnimalStatuses.FirstOrDefaultAsync(s => s.FarmId == farmId && s.Name == "Sold")
            ?? throw new InvalidOperationException("Sold status missing after defaults seed.");

        var cattle = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-1001", Name = "Bella",
            AnimalType = cattleType, Breed = breed,
            SexOption = female, AnimalStatus = activeStatus,
            Location = location, DateOfBirth = now.AddYears(-3).AddMonths(-2),
            AcquisitionDate = now.AddYears(-3), Notes = "Star cow, top milk yield."
        };
        var karim = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-1002", Name = "Karima",
            AnimalType = cattleType, Breed = breed,
            SexOption = female, AnimalStatus = pregnantStatus,
            Location = location, DateOfBirth = now.AddYears(-2).AddMonths(-8),
            AcquisitionDate = now.AddYears(-2)
        };
        var nasra = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-1003", Name = "Nasra",
            AnimalType = cattleType, Breed = breed,
            SexOption = female, AnimalStatus = activeStatus,
            Location = location, DateOfBirth = now.AddYears(-4),
            AcquisitionDate = now.AddYears(-4), Notes = "Older cow, steady producer."
        };
        var duna = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-1004", Name = "Duna",
            AnimalType = cattleType, Breed = breed,
            SexOption = female, AnimalStatus = dryStatus,
            Location = location, DateOfBirth = now.AddYears(-1).AddMonths(-5),
            AcquisitionDate = now.AddYears(-1)
        };
        var sudan = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-1005", Name = "Sudan",
            AnimalType = cattleType, Breed = breed,
            SexOption = male, AnimalStatus = activeStatus,
            Location = location, DateOfBirth = now.AddYears(-2),
            AcquisitionDate = now.AddYears(-2)
        };
        var sick1 = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-2001", Name = "Fadila",
            AnimalType = cattleType, Breed = breed,
            SexOption = female, AnimalStatus = sickStatus,
            Location = location, DateOfBirth = now.AddYears(-3).AddMonths(-1),
            AcquisitionDate = now.AddYears(-3), Notes = "On treatment for foot infection."
        };
        var sick2 = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-2002", Name = "Rudaina",
            AnimalType = cattleType, Breed = breed,
            SexOption = female, AnimalStatus = sickStatus,
            Location = location, DateOfBirth = now.AddYears(-2).AddMonths(-9),
            AcquisitionDate = now.AddYears(-2), Notes = "Recovering from mastitis."
        };
        var sold = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-3001", Name = "Mariam",
            AnimalType = cattleType, Breed = breed,
            SexOption = female, AnimalStatus = soldStatus,
            Location = location, DateOfBirth = now.AddYears(-5),
            AcquisitionDate = now.AddYears(-5), Notes = "Sold at auction, March."
        };
        // An edge case: an animal with no name (tag-only record) — demonstrates the empty-name path.
        var tagOnly = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            TagNumber = "SR-4001",
            AnimalType = cattleType, Breed = breed,
            SexOption = male, AnimalStatus = activeStatus,
            Location = location, DateOfBirth = now.AddMonths(-6),
            AcquisitionDate = now.AddMonths(-6)
        };
        context.Animals.AddRange(cattle, karim, nasra, duna, sudan, sick1, sick2, sold, tagOnly);

        // ── Weights: a recent weight and an older one, so the weight report has history ───
        context.WeightRecords.Add(new WeightRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = cattle.Id,
            WeightKg = 487, RecordedAt = now.AddDays(-14)
        });
        context.WeightRecords.Add(new WeightRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = cattle.Id,
            WeightKg = 479, RecordedAt = now.AddDays(-44)
        });
        context.WeightRecords.Add(new WeightRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = nasra.Id,
            WeightKg = 412, RecordedAt = now.AddDays(-30)
        });

        // ── Health: open, in-progress and resolved medical records, a medicine, a vaccine ──
        var oxy = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Oxytetracycline", "ml", 50, 30, 100, 2.50m, "BATCH-OXY-001", now.AddDays(60), now.AddDays(-30));
        var vetmed = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Meloxicam", "ml", 30, 21, 60, 1.80m, "BATCH-MEL-001", now.AddDays(90), now.AddDays(-10));

        var fmdVax = await EnsureVaccineTypeAsync(context, farmId, now,
            "Foot and Mouth Disease Vaccine", "5ml", oxy.Id);

        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = sick1.Id,
            Symptoms = "Limping on front left leg, swelling above hoof.",
            Diagnosis = "Foot infection (foot rot)",
            Treatment = "Oxytetracycline spray + systemic antibiotics",
            MedicineUsed = "Oxytetracycline",
            Dosage = "10ml IM daily x 5 days",
            VetName = FullName(vet), Cost = 75m,
            DateRecorded = now.AddDays(-7), FollowUpDate = now.AddDays(5),
            Status = MedicalRecordStatus.Open, Notes = "Improving; recheck in 5 days."
        });
        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = sick2.Id,
            Symptoms = "Swollen udder, reduced milk, mild fever.",
            Diagnosis = "Clinical mastitis",
            Treatment = "Meloxicam + intramammary antibiotic",
            MedicineUsed = "Meloxicam",
            Dosage = "15ml IM + intramammary tube",
            VetName = FullName(vet), Cost = 55m,
            DateRecorded = now.AddDays(-12), FollowUpDate = now.AddDays(3),
            Status = MedicalRecordStatus.InProgress
        });
        context.MedicalRecords.Add(new MedicalRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = nasra.Id,
            Symptoms = "Wound on flank from barbed wire.",
            Diagnosis = "Superficial wound",
            Treatment = "Clean, topical antibiotic, tetanus booster",
            MedicineUsed = "Oxytetracycline",
            Dosage = "Topical spray daily",
            VetName = FullName(vet), Cost = 18m,
            DateRecorded = now.AddDays(-40), Status = MedicalRecordStatus.Resolved
        });

        // A vaccination record linked to a medicine batch (FIFO deduction is a feature to show).
        context.VaccinationRecords.Add(new VaccinationRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = cattle.Id,
            VaccineTypeId = fmdVax.Id, DateGiven = now.AddDays(-15),
            VetName = FullName(vet), BatchNumber = oxy.StockBatches.First().BatchNumber,
            Cost = 12m, Notes = "Routine preventative booster."
        });

        // A vaccination schedule (so the dashboard's due/overdue snapshot has something).
        context.VaccinationSchedules.Add(new VaccinationSchedule
        {
            Id = Guid.NewGuid(), FarmId = farmId, VaccineTypeId = fmdVax.Id,
            AnimalTypeId = cattleType.Id, RecurrenceDays = 180,
            IsActive = true, Notes = "Annual FMD booster for the milking herd.",
            CreatedAt = now.AddMonths(-6)
        });
        // A weight check schedule (demo of the weight-check due/overdue path).
        context.WeightCheckSchedules.Add(new WeightCheckSchedule
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalTypeId = cattleType.Id,
            AgeCategoryId = null, RecurrenceDays = 60,
            IsActive = true, Notes = "Monthly weight check for the milking herd.",
            CreatedAt = now.AddMonths(-3)
        });

        // ── Medicine alerts: one low-stock, one expiring-soon, one expired ───────────────
        context.MedicineStocks.Add(new MedicineStock
        {
            Id = Guid.NewGuid(), FarmId = farmId, MedicineId = oxy.Id,
            BatchNumber = "BATCH-OXY-002", Quantity = 8, UnitCost = 2.50m,
            ExpiryDate = now.AddDays(-5), DateReceived = now.AddMonths(-4),
            Supplier = "Sunrise Feeds Co."
        }); // expired batch (alert)

        // ── Breeding: a confirmed pregnancy with an upcoming birth ───────────────────────
        var breedingRecord = new BreedingRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            SireId = sudan.Id, DamId = karim.Id,
            BreedingDate = now.AddDays(-200), Method = BreedingMethod.Natural,
            VetName = FullName(vet), Result = BreedingResult.Confirmed,
            Notes = "Natural mating, observed on heat."
        };
        context.BreedingRecords.Add(breedingRecord);
        context.GestationRecords.Add(new GestationRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            BreedingRecordId = breedingRecord.Id,
            AnimalId = karim.Id, ConfirmedDate = now.AddDays(-180),
            ExpectedDeliveryDate = now.AddDays(10), CurrentStage = GestationStage.Late,
            HealthCheckNotes = "Healthy progression, no concerns."
        });
        // A second breeding that failed (edge case: failed result, no gestation).
        context.BreedingRecords.Add(new BreedingRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            SireId = sudan.Id, DamId = duna.Id,
            BreedingDate = now.AddDays(-60), Method = BreedingMethod.Natural,
            Result = BreedingResult.Failed,
            Notes = "Returned to heat after 21 days, re-bred."
        });

        // ── Birth record: a past birth with two offspring (one alive, one stillborn) ──────
        var birthDam = await context.Animals.FirstOrDefaultAsync(a => a.TagNumber == "SR-1003")
            ?? throw new InvalidOperationException("Birth dam missing.");
        var birthRecordId = Guid.NewGuid();
        context.BirthRecords.Add(new BirthRecord
        {
            Id = birthRecordId, FarmId = farmId, DamId = birthDam.Id,
            BirthDate = now.AddMonths(-2), OffspringCount = 2,
            VetName = FullName(vet), Notes = "Twins; one stillborn, one healthy heifer.",
            Offspring = new List<BirthOffspring>
            {
                new() { Id = Guid.NewGuid(), FarmId = farmId, BirthRecordId = birthRecordId,
                    TagNumber = "SR-1003-A", SexOptionId = female.Id, Outcome = BirthOutcome.Alive, BirthWeightKg = 38, Notes = "Healthy heifer, kept as replacement." },
                new() { Id = Guid.NewGuid(), FarmId = farmId, BirthRecordId = birthRecordId,
                    TagNumber = "SR-1003-B", SexOptionId = female.Id, Outcome = BirthOutcome.Stillborn, Notes = "Stillborn, no signs of life." }
            }
        });

        // ── Feed: types, stock, movements, records, a diet plan, a schedule, a generated task ──
        var hay = await EnsureFeedTypeAsync(context, farmId, now, "Lucerne Hay", FeedCategory.Forage, FeedUnit.Bale, 18m, "High-quality baled lucerne.");
        var silage = await EnsureFeedTypeAsync(context, farmId, now, "Maize Silage", FeedCategory.Forage, FeedUnit.Ton, 45m, "Home-grown maize silage.");
        var dairyPE = await EnsureFeedTypeAsync(context, farmId, now, "Dairy Protein Concentrate", FeedCategory.Concentrate, FeedUnit.Kilogram, 920m, "High-protein concentrate for the milking herd.");
        var mineral = await EnsureFeedTypeAsync(context, farmId, now, "Dairy Mineral Block", FeedCategory.Mineral, FeedUnit.Other, 22m, "Rigid mineral block, ad lib.");

        await EnsureFeedStockMovementAsync(context, farmId, now, hay.Id, StockMovementType.Purchase, 24, hay.CostPerUnit, "Sunrise Feeds Co.", now.AddDays(-30));
        await EnsureFeedStockMovementAsync(context, farmId, now, hay.Id, StockMovementType.Consumption, 8, hay.CostPerUnit, null, now.AddDays(-10));
        await EnsureFeedStockMovementAsync(context, farmId, now, silage.Id, StockMovementType.Purchase, 6, silage.CostPerUnit, "Home grown", now.AddDays(-45));
        await EnsureFeedStockMovementAsync(context, farmId, now, dairyPE.Id, StockMovementType.Purchase, 120, dairyPE.CostPerUnit, "DairyCo Feeds", now.AddDays(-7));
        await EnsureFeedStockMovementAsync(context, farmId, now, mineral.Id, StockMovementType.Purchase, 12, mineral.CostPerUnit, "AgriMinerals", now.AddDays(-20));

        context.FeedRecords.Add(new FeedRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = hay.Id,
            AnimalId = cattle.Id, Quantity = 2, UnitCost = hay.CostPerUnit,
            FedAt = now.AddDays(-1),
            Notes = "Morning feed, bale split across the herd."
        });
        context.FeedRecords.Add(new FeedRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = dairyPE.Id,
            LocationId = location.Id, Quantity = 45, UnitCost = dairyPE.CostPerUnit,
            FedAt = now.AddDays(-1),
            Notes = "Evening concentrate, fed at the main barn."
        });

        var dietPlan = await EnsureDietPlanAsync(context, farmId, now, cattleType.Id, breed.Id, "Milking Cow Ration",
            "Balanced ration for mid-lactation cows.", [
                (dairyPE.Id, 5.5m),
                (hay.Id, 12m),
                (mineral.Id, 0.5m)
            ]);
        await EnsureFeedingScheduleAsync(context, farmId, now, dietPlan.Id, TimeSpan.FromHours(6), "Morning milking feed");
        await EnsureFeedingScheduleAsync(context, farmId, now, dietPlan.Id, new TimeSpan(17, 30, 0), "Evening feed");

        var morningSchedule = await context.FeedingSchedules.FirstOrDefaultAsync(s => s.DietPlanId == dietPlan.Id && s.Label == "Morning milking feed")
            ?? throw new InvalidOperationException("Morning feeding schedule missing.");
        context.FeedingTasks.Add(new FeedingTask
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            FeedingScheduleId = morningSchedule.Id, DietPlanId = dietPlan.Id,
            TaskDate = now.AddDays(-1), TimeOfDay = TimeSpan.FromHours(6),
            Status = FeedingTaskStatus.Completed, CompletedAt = now.AddDays(-1).AddHours(6),
            TargetAnimalCount = 5, Notes = "Fed on time. Carried out by the vet."
        });
        var eveningSchedule = await context.FeedingSchedules.FirstOrDefaultAsync(s => s.DietPlanId == dietPlan.Id && s.Label == "Evening feed")
            ?? throw new InvalidOperationException("Evening feeding schedule missing.");
        context.FeedingTasks.Add(new FeedingTask
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            FeedingScheduleId = eveningSchedule.Id, DietPlanId = dietPlan.Id,
            TaskDate = now.Date, TimeOfDay = new TimeSpan(17, 30, 0),
            Status = FeedingTaskStatus.Pending,
            TargetAnimalCount = 5
        });

        // ── Inventory: items, stock movements, a supplier, a customer ───────────────────
        var hayBales = await EnsureInventoryItemAsync(context, farmId, now,
            "Lucerne Hay Bales", "Forage", "Bale", 18m, 12, 10);
        var silageBags = await EnsureInventoryItemAsync(context, farmId, now,
            "Maize Silage Bags", "Forage", "Bag", 25m, 40, 20);
        var vaccines = await EnsureInventoryItemAsync(context, farmId, now,
            "FMD Vaccine Doses", "Health", "Dose", 12m, 8, 15);

        await EnsureStockMovementAsync(context, farmId, now, hayBales.Id, InventoryMovementType.Purchase, 24, 18m, "Sunrise Feeds Co.");
        await EnsureStockMovementAsync(context, farmId, now, hayBales.Id, InventoryMovementType.Consumption, 12, 18m, null);
        await EnsureStockMovementAsync(context, farmId, now, silageBags.Id, InventoryMovementType.Purchase, 40, 25m, "Home grown");
        await EnsureStockMovementAsync(context, farmId, now, vaccines.Id, InventoryMovementType.Purchase, 20, 12m, "AgriSupply");

        var supplier = await EnsureSupplierAsync(context, farmId, now,
            "Sunrise Feeds Co.", "feed@sunrisefeeds.example", "Farm feeds, forage and concentrates");
        var customer = await EnsureCustomerAsync(context, farmId, now,
            "Green Valley Dairy", "sales@greenvalley.example");

        await EnsureSupplierPurchaseAsync(context, farmId, now, supplier.Id, hayBales.Id, 24, 432m, "Prevailing price", now.AddDays(-30));
        await EnsureCustomerSaleAsync(context, farmId, now, customer.Id, hayBales.Id, 6, 132m, "Spot sale", now.AddDays(-5));

        // ── Finance: expenses and income with real dates ─────────────────────────────────
        context.Expenses.Add(new Expense
        {
            Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = now.AddDays(-10),
            Amount = 432m, ExpenseCategoryId = feedCat.Id, PaymentMethodId = cash.Id,
            Description = "Feed purchase — lucerne hay (Sunrise Feeds Co.)"
        });
        context.Expenses.Add(new Expense
        {
            Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = now.AddDays(-14),
            Amount = 75m, ExpenseCategoryId = healthCat.Id, PaymentMethodId = cash.Id,
            AnimalId = sick1.Id, Description = "Veterinary treatment — foot infection (Fadila)"
        });
        context.Expenses.Add(new Expense
        {
            Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = now.AddDays(-3),
            Amount = 320m, ExpenseCategoryId = maintCat.Id, PaymentMethodId = bank.Id,
            Description = "Repair of south fence, paddock 2"
        });
        context.IncomeRecords.Add(new IncomeRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, IncomeDate = now.AddDays(-5),
            Amount = 1_450m, IncomeCategoryId = milkCat.Id, PaymentMethodId = bank.Id,
            AnimalId = cattle.Id, Description = "Two weeks milk sales to Green Valley Dairy"
        });
        context.IncomeRecords.Add(new IncomeRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, IncomeDate = now.AddDays(-2),
            Amount = 1_200m, IncomeCategoryId = livestockCat.Id, PaymentMethodId = bank.Id,
            Description = "Sale of two feeder steers to livestock market"
        });

        // ── Farm tasks: overdue high-priority, a completed one, a cancelled one, an in-progress ──
        context.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Title = "Repair south fence — paddock 2",
            Description = "Gap in fencing, animals at risk of wandering. Coordinating with local fencer.",
            Priority = FarmTaskPriority.High, Status = FarmTaskStatus.Pending,
            DueDate = now.AddDays(-3), AssignedEmployeeId = worker.Id
        });
        context.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Title = "Vaccination round — FMD booster",
            Description = "Annual FMD booster for the milking herd.",
            Priority = FarmTaskPriority.Medium, Status = FarmTaskStatus.Completed,
            DueDate = now.AddDays(-7), AssignedEmployeeId = worker.Id,
            CompletedAt = now.AddDays(-6), CompletionNotes = "All 24 animals vaccinated. Carried out by the vet."
        });
        context.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Title = "Prepare silage face for winter feeding",
            Description = "Trim and re-pack the silage face ahead of winter feeding.",
            Priority = FarmTaskPriority.Medium, Status = FarmTaskStatus.InProgress,
            DueDate = now.AddDays(4), AssignedEmployeeId = manager.Id
        });
        context.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            Title = "Replace worn floodlight in dairy shed",
            Description = "Floodlight at the milking parlour is failing.",
            Priority = FarmTaskPriority.Low, Status = FarmTaskStatus.Cancelled,
            DueDate = now.AddDays(-10), CancelReason = "Replaced under maintenance schedule instead."
        });

        // ── Attendance: a mix of statuses for the current and previous week ───────────────
        await EnsureAttendanceAsync(context, farmId, now, vet.Id, AttendanceStatus.Present, now.AddDays(-1));
        await EnsureAttendanceAsync(context, farmId, now, vet.Id, AttendanceStatus.Present, now.AddDays(-2));
        await EnsureAttendanceAsync(context, farmId, now, worker.Id, AttendanceStatus.Present, now.AddDays(-1));
        await EnsureAttendanceAsync(context, farmId, now, worker.Id, AttendanceStatus.Late, now.AddDays(-3));
        await EnsureAttendanceAsync(context, farmId, now, worker.Id, AttendanceStatus.Absent, now.AddDays(-5));
        await EnsureAttendanceAsync(context, farmId, now, manager.Id, AttendanceStatus.Present, now.AddDays(-1));
        await EnsureAttendanceAsync(context, farmId, now, manager.Id, AttendanceStatus.HalfDay, now.AddDays(-4));

        // ── Performance reviews: two completed reviews ───────────────────────────────────
        context.PerformanceReviews.Add(new PerformanceReview
        {
            Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = vet.Id,
            Rating = 4, ReviewDate = now.AddMonths(-2),
            PeriodStart = now.AddMonths(-8), PeriodEnd = now.AddMonths(-2),
            Strengths = "Excellent clinical work, responsive to ward calls.",
            AreasForImprovement = "Case notes could be more detailed.",
            ReviewedBy = manager.Id
        });
        context.PerformanceReviews.Add(new PerformanceReview
        {
            Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = worker.Id,
            Rating = 3, ReviewDate = now.AddMonths(-1),
            PeriodStart = now.AddMonths(-7), PeriodEnd = now.AddMonths(-1),
            Strengths = "Reliable, good animal handling.",
            AreasForImprovement = "Time management on multi-task days.",
            ReviewedBy = manager.Id
        });

        // ── Salary payments: recent monthly payments for the team ───────────────────────
        context.SalaryPayments.Add(new SalaryPayment
        {
            Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = vet.Id,
            Amount = 9_500m, PaymentDate = now.AddDays(-28),
            SalaryType = SalaryType.Monthly, PaymentType = SalaryPaymentType.BankTransfer,
            PeriodCovered = new DateTime(now.Year, now.Month, 1),
            Reference = "SAL-2026-" + now.Month,
            Notes = "Monthly salary — March."
        });
        context.SalaryPayments.Add(new SalaryPayment
        {
            Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = worker.Id,
            Amount = 3_100m, PaymentDate = now.AddDays(-28),
            SalaryType = SalaryType.Monthly, PaymentType = SalaryPaymentType.BankTransfer,
            PeriodCovered = new DateTime(now.Year, now.Month, 1),
            Reference = "SAL-2026-" + now.Month,
            Notes = "Monthly salary — March."
        });
        context.SalaryPayments.Add(new SalaryPayment
        {
            Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = manager.Id,
            Amount = 6_200m, PaymentDate = now.AddDays(-28),
            SalaryType = SalaryType.Monthly, PaymentType = SalaryPaymentType.BankTransfer,
            PeriodCovered = new DateTime(now.Year, now.Month, 1),
            Reference = "SAL-2026-" + now.Month,
            Notes = "Monthly salary — March."
        });
    }

    // ── tiny helpers ──────────────────────────────────────────────────────────────────────

    private static string FullName(Employee e) => $"{e.FirstName} {e.LastName}".Trim();

    private static async Task<Role?> EnsureSystemOwnerRoleAsync(FmsDbContext context, DateTime now)
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
        Guid animalTypeId, Guid breedId, string name, string notes, (Guid FeedTypeId, decimal QuantityPerFeeding)[] items)
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

    private static async Task EnsureFeedingScheduleAsync(FmsDbContext context, Guid farmId, DateTime now,
        Guid dietPlanId, TimeSpan timeOfDay, string label)
    {
        context.FeedingSchedules.Add(new FeedingSchedule
        {
            Id = Guid.NewGuid(), FarmId = farmId, DietPlanId = dietPlanId,
            TimeOfDay = timeOfDay, Label = label,
            IsActive = true
        });
        await context.SaveChangesAsync();
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
