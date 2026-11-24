using FMS.Application.Common;
using FMS.Application.Farm;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Farm;

public static partial class DemoDataSeeder
{
    /// <summary>
    /// The demo farm's domain data. Every date is relative to <paramref name="now"/>, and the
    /// counts are deliberately fixed so the demo-smoke tests can assert each module is populated.
    /// This runs only after <see cref="ResetDemoDataAsync"/> has wiped the demo farm, or on the
    /// lazy first-login path when no demo user exists.
    /// </summary>
    // ── domain data seed ──────────────────────────────────────────────────────────────────

    private static async Task SeedEverythingAsync(FmsDbContext context, Guid farmId, Guid demoUserId, DateTime now)
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

        // The blocks above give every screen a first page of data. The enrichment below adds
        // the history that makes the demo feel like a working farm: multiple species, clinical
        // and stock narratives, several months of ledger entries and a populated alert centre.
        await SeedRicherDemoHistoryAsync(context, farmId, demoUserId, now);
    }

    // ── richer demo history ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds believable depth to the demo farm without touching any other farm. Each helper
    /// re-reads the anchors it needs by their stable tag/name, so the skeleton seed above can
    /// stay focused on "one meaningful row per screen" and this method can be tuned independently.
    /// </summary>
    private static async Task SeedRicherDemoHistoryAsync(FmsDbContext context, Guid farmId, Guid demoUserId, DateTime now)
    {
        await SeedDemoVetsAsync(context, farmId, now);
        await SeedDemoAnimalDetailAsync(context, farmId, now);
        await SeedDemoClinicalHistoryAsync(context, farmId, now);
        await SeedDemoFeedHistoryAsync(context, farmId, now);
        await SeedDemoTradeAndFinanceHistoryAsync(context, farmId, now);
        await SeedDemoStaffAndTaskHistoryAsync(context, farmId, now);
        await SeedDemoNotificationsAsync(context, farmId, demoUserId, now);
    }

    /// <summary>
    /// The Vets screen has no data until a vet is added by hand, yet three of the seeded
    /// clinical records already name a vet. Seed the contacts those records refer to (plus a
    /// retired one so the Active/Inactive toggle has something to show) and link the records.
    /// </summary>
    private static async Task SeedDemoVetsAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        var aisha = await EnsureVetAsync(context, farmId, now, "Dr. Aisha Karim",
            "a.karim@sunriseranch.example", "+1 555 0101", "VET-4821",
            "Herd health, reproduction and emergency cover.");
        var peter = await EnsureVetAsync(context, farmId, now, "Dr. Peter Njoroge",
            "p.njoroge@sunriseranch.example", "+1 555 0102", "VET-5176",
            "Routine visits and vaccination rounds.");
        var leila = await EnsureVetAsync(context, farmId, now, "Dr. Leila Hassan",
            "l.hassan@sunriseranch.example", "+1 555 0103", "VET-2290",
            "Historical records only; no longer on call.");
        leila.IsActive = false;
        leila.ModifiedAt = now;

        foreach (var record in await context.MedicalRecords
                     .Where(m => m.FarmId == farmId && m.VetId == null).ToListAsync())
            record.VetId = aisha.Id;
        foreach (var record in await context.VaccinationRecords
                     .Where(v => v.FarmId == farmId && v.VetId == null).ToListAsync())
            record.VetId = peter.Id;
        foreach (var record in await context.BreedingRecords
                     .Where(b => b.FarmId == farmId && b.VetId == null).ToListAsync())
            record.VetId = aisha.Id;
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A one-location, cattle-only herd leaves the animal filters, lineage view and transfer
    /// history looking empty. Add two more species, a location tree, ear-tag/RFID identifiers
    /// and movement history so those screens have something real to display.
    /// </summary>
    private static async Task SeedDemoAnimalDetailAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        var northPaddock = await EnsureLocationAsync(context, farmId, now, "North Paddock", "Paddock", null);
        var calfPen = await EnsureLocationAsync(context, farmId, now, "Calf Pen", "Pen", northPaddock.Id);
        var isolationPen = await EnsureLocationAsync(context, farmId, now, "Isolation Pen", "Pen", northPaddock.Id);

        var earTag = await EnsureIdentificationTypeAsync(context, farmId, "Ear Tag");
        var rfid = await EnsureIdentificationTypeAsync(context, farmId, "RFID");
        var brand = await EnsureIdentificationTypeAsync(context, farmId, "Brand");

        var cattleType = await context.AnimalTypes.FirstAsync(t => t.FarmId == farmId && t.Name == "Cattle");
        var sahiwal = await context.Breeds.FirstAsync(b => b.AnimalTypeId == cattleType.Id && b.Name == "Sahiwal");
        var holstein = await context.Breeds.FirstAsync(b => b.AnimalTypeId == cattleType.Id && b.Name == "Holstein Friesian");
        var buffaloType = await context.AnimalTypes.FirstAsync(t => t.FarmId == farmId && t.Name == "Buffalo");
        var murrah = await context.Breeds.FirstAsync(b => b.AnimalTypeId == buffaloType.Id && b.Name == "Murrah");
        var goatType = await context.AnimalTypes.FirstAsync(t => t.FarmId == farmId && t.Name == "Goat");
        var beetal = await context.Breeds.FirstAsync(b => b.AnimalTypeId == goatType.Id && b.Name == "Beetal");
        var boer = await context.Breeds.FirstAsync(b => b.AnimalTypeId == goatType.Id && b.Name == "Boer");
        var male = await context.SexOptions.FirstAsync(s => s.FarmId == farmId && s.Value == "Male");
        var female = await context.SexOptions.FirstAsync(s => s.FarmId == farmId && s.Value == "Female");
        var active = await context.AnimalStatuses.FirstAsync(s => s.FarmId == farmId && s.Name == "Active");
        var lactating = await context.AnimalStatuses.FirstAsync(s => s.FarmId == farmId && s.Name == "Lactating");
        var sick = await context.AnimalStatuses.FirstAsync(s => s.FarmId == farmId && s.Name == "Sick");
        var mainBarn = await context.Locations.FirstAsync(l => l.FarmId == farmId && l.Name == "Main Barn");

        Animal NewAnimal(string tag, string? name, AnimalType type, Breed breed, SexOption sex,
            AnimalStatus status, Guid locationId, DateTime dob, DateTime acquired, string? notes = null)
        {
            var animal = new Animal
            {
                Id = Guid.NewGuid(), FarmId = farmId, TagNumber = tag, Name = name,
                AnimalTypeId = type.Id, BreedId = breed.Id, SexOptionId = sex.Id,
                AnimalStatusId = status.Id, LocationId = locationId,
                DateOfBirth = dob, AcquisitionDate = acquired, Notes = notes,
                CreatedAt = acquired
            };
            context.Animals.Add(animal);
            return animal;
        }

        var zayna = NewAnimal("SR-1006", "Zayna", cattleType, holstein, female, lactating, mainBarn.Id,
            now.AddYears(-3).AddMonths(-4), now.AddYears(-3).AddMonths(-3), "High-yielding Holstein, second lactation.");
        var hana = NewAnimal("SR-1007", "Hana", cattleType, sahiwal, female, active, northPaddock.Id,
            now.AddYears(-2), now.AddYears(-2));
        var omar = NewAnimal("SR-1008", "Omar", cattleType, holstein, male, active, northPaddock.Id,
            now.AddMonths(-18), now.AddMonths(-17), "Young bull, being grown out for breeding.");
        var rania = NewAnimal("SR-1009", "Rania", cattleType, sahiwal, female, sick, isolationPen.Id,
            now.AddYears(-5), now.AddYears(-5), "Being treated for respiratory infection — kept separate from the herd.");
        var sana = NewAnimal("BF-3001", "Sana", buffaloType, murrah, female, lactating, northPaddock.Id,
            now.AddYears(-4), now.AddYears(-4), "Buffalo milk for the local market.");
        var tariq = NewAnimal("BF-3002", "Tariq", buffaloType, murrah, male, active, northPaddock.Id,
            now.AddYears(-2).AddMonths(-3), now.AddYears(-2));
        var biscuit = NewAnimal("GT-2001", "Biscuit", goatType, beetal, female, active, calfPen.Id,
            now.AddMonths(-14), now.AddMonths(-13));
        var cocoa = NewAnimal("GT-2002", "Cocoa", goatType, boer, male, active, calfPen.Id,
            now.AddMonths(-10), now.AddMonths(-9));
        var ginger = NewAnimal("GT-2003", "Ginger", goatType, beetal, female, active, calfPen.Id,
            now.AddMonths(-8), now.AddMonths(-8), "Gentle milker, used for the kids.");
        await context.SaveChangesAsync();

        // Identifiers: every new animal gets a primary ear tag; a few also carry an RFID chip or
        // a brand, so the "identifications" list is not a single repeated row.
        var identificationRows = new (Guid AnimalId, Guid TypeId, string Value, bool Primary, int DaysAgo)[]
        {
            (zayna.Id, earTag.Id, "EAR-SR-1006", true, 900),
            (zayna.Id, rfid.Id, "RFID-9820004112", false, 900),
            (hana.Id, earTag.Id, "EAR-SR-1007", true, 700),
            (sana.Id, earTag.Id, "EAR-BF-3001", true, 1400),
            (sana.Id, brand.Id, "SR-BUFF-01", false, 1400),
            (biscuit.Id, earTag.Id, "EAR-GT-2001", true, 380),
            (cocoa.Id, earTag.Id, "EAR-GT-2002", true, 300),
            (ginger.Id, earTag.Id, "EAR-GT-2003", true, 240),
            (omar.Id, rfid.Id, "RFID-9820007889", true, 500)
        };
        context.AnimalIdentifications.AddRange(identificationRows.Select(i => new AnimalIdentification
        {
            Id = Guid.NewGuid(), FarmId = farmId, AnimalId = i.AnimalId,
            IdentificationTypeId = i.TypeId, Value = i.Value,
            IsPrimary = i.Primary, DateAttached = now.AddDays(-i.DaysAgo)
        }));

        // A short movement trail: the goats moved into the calf pen, the sick cow into isolation,
        // and one heifer came in from the north paddock.
        context.AnimalTransfers.AddRange(
            new AnimalTransfer
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = biscuit.Id,
                FromLocationId = mainBarn.Id, ToLocationId = calfPen.Id,
                TransferredAt = now.AddDays(-120), Reason = "Moved in with the other milking goats."
            },
            new AnimalTransfer
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = ginger.Id,
                FromLocationId = northPaddock.Id, ToLocationId = calfPen.Id,
                TransferredAt = now.AddDays(-60), Reason = "Brought in for the season."
            },
            new AnimalTransfer
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = rania.Id,
                FromLocationId = mainBarn.Id, ToLocationId = isolationPen.Id,
                TransferredAt = now.AddDays(-9), Reason = "Isolated while respiratory infection is treated."
            });
        await context.SaveChangesAsync();

        // Weigh history: a multi-month series for the star cow and a shorter one for each new
        // animal, so the weight report and growth chart show a trend rather than single points.
        var series = new (Guid AnimalId, decimal[] Weights, int StartDaysAgo)[]
        {
            (await context.Animals.Where(a => a.TagNumber == "SR-1001").Select(a => a.Id).FirstAsync(),
                [474m, 479m, 483m, 487m], 105),
            (zayna.Id, [430m, 441m, 452m, 461m], 100),
            (hana.Id, [388m, 394m, 401m], 80),
            (sana.Id, [512m, 520m, 526m], 90),
            (omar.Id, [290m, 312m, 335m], 75),
            (biscuit.Id, [34m, 37m, 39m], 70),
            (cocoa.Id, [28m, 32m, 36m], 60),
            (ginger.Id, [31m, 33m], 45)
        };
        var weightRows = new List<WeightRecord>();
        foreach (var (animalId, weights, startDaysAgo) in series)
        {
            for (var i = 0; i < weights.Length; i++)
            {
                weightRows.Add(new WeightRecord
                {
                    Id = Guid.NewGuid(), FarmId = farmId, AnimalId = animalId,
                    WeightKg = weights[i], RecordedAt = now.AddDays(-(startDaysAgo - i * 30)),
                    Notes = "Routine weigh-in."
                });
            }
        }
        context.WeightRecords.AddRange(weightRows);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Adds clinical depth: more diagnoses, a second vaccine with its own schedule, and the
    /// health-check history the gestation screen expects for a confirmed pregnancy.
    /// </summary>
    private static async Task SeedDemoClinicalHistoryAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        var aisha = await context.Vets.FirstAsync(v => v.FarmId == farmId && v.Name == "Dr. Aisha Karim");
        var peter = await context.Vets.FirstAsync(v => v.FarmId == farmId && v.Name == "Dr. Peter Njoroge");
        var fadila = await context.Animals.FirstAsync(a => a.TagNumber == "SR-2001");
        var rudaina = await context.Animals.FirstAsync(a => a.TagNumber == "SR-2002");
        var rania = await context.Animals.FirstAsync(a => a.TagNumber == "SR-1009");
        var zayna = await context.Animals.FirstAsync(a => a.TagNumber == "SR-1006");
        var biscuit = await context.Animals.FirstAsync(a => a.TagNumber == "GT-2001");
        var ginger = await context.Animals.FirstAsync(a => a.TagNumber == "GT-2003");
        var sana = await context.Animals.FirstAsync(a => a.TagNumber == "BF-3001");

        context.MedicalRecords.AddRange(
            new MedicalRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = rania.Id,
                Symptoms = "Coughing, nasal discharge and reduced appetite.",
                Diagnosis = "Bovine respiratory disease", Treatment = "Antibiotics + supportive care",
                MedicineUsed = "Oxytetracycline", Dosage = "20ml IM daily x 5 days",
                VetId = aisha.Id, VetName = aisha.Name, Cost = 96m,
                DateRecorded = now.AddDays(-9), FollowUpDate = now.AddDays(2),
                Status = MedicalRecordStatus.InProgress, Notes = "Isolated in the isolation pen."
            },
            new MedicalRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = rudaina.Id,
                Symptoms = "Mastitis follow-up, udder swelling much reduced.",
                Diagnosis = "Clinical mastitis — resolving", Treatment = "Continue anti-inflammatory",
                MedicineUsed = "Meloxicam", Dosage = "10ml IM once daily x 2 days",
                VetId = aisha.Id, VetName = aisha.Name, Cost = 28m,
                DateRecorded = now.AddDays(-3), Status = MedicalRecordStatus.InProgress
            },
            new MedicalRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = biscuit.Id,
                Symptoms = "Scouring and weight loss over three days.",
                Diagnosis = "Goat coccidiosis", Treatment = "Oral anticoccidial + electrolytes",
                MedicineUsed = "Coccidiostat suspension", Dosage = "5ml orally daily x 3 days",
                VetId = peter.Id, VetName = peter.Name, Cost = 22m,
                DateRecorded = now.AddDays(-18), Status = MedicalRecordStatus.Resolved,
                Notes = "Full recovery; pen cleaned and bedding replaced."
            },
            new MedicalRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = zayna.Id,
                Symptoms = "Routine post-calving check — no abnormalities.",
                Diagnosis = "Post-partum check", Treatment = "None required",
                VetId = aisha.Id, VetName = aisha.Name, Cost = 35m,
                DateRecorded = now.AddDays(-26), Status = MedicalRecordStatus.Resolved
            });

        var brucella = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Brucellosis Vaccine", "dose", 20, 60, 40, 1.40m, "BATCH-BRU-001", now.AddDays(120), now.AddDays(-20));
        var fmd = await context.VaccineTypes.FirstAsync(v => v.FarmId == farmId && v.Name == "Foot and Mouth Disease Vaccine");
        var brucellaVax = await EnsureVaccineTypeAsync(context, farmId, now,
            "Brucellosis Vaccine", "2ml", brucella.Id);

        var oxyBatch = await context.MedicineStocks
            .Where(s => s.FarmId == farmId && s.BatchNumber == "BATCH-OXY-001")
            .Select(s => s.BatchNumber).FirstAsync();
        context.VaccinationRecords.AddRange(
            new VaccinationRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = zayna.Id,
                VaccineTypeId = fmd.Id, DateGiven = now.AddDays(-40),
                VetId = peter.Id, VetName = peter.Name, BatchNumber = oxyBatch,
                Cost = 12m, Notes = "Post-calving booster."
            },
            new VaccinationRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId,
                AnimalId = await context.Animals.Where(a => a.TagNumber == "SR-1007").Select(a => a.Id).FirstAsync(),
                VaccineTypeId = fmd.Id, DateGiven = now.AddDays(-25),
                VetId = peter.Id, VetName = peter.Name, BatchNumber = oxyBatch, Cost = 12m
            },
            new VaccinationRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = sana.Id,
                VaccineTypeId = brucellaVax.Id, DateGiven = now.AddDays(-35),
                VetId = peter.Id, VetName = peter.Name, BatchNumber = "BATCH-BRU-001",
                Cost = 15m, Notes = "Annual buffalo brucellosis vaccination."
            },
            new VaccinationRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = ginger.Id,
                VaccineTypeId = fmd.Id, DateGiven = now.AddDays(-14),
                VetId = peter.Id, VetName = peter.Name, Cost = 8m,
                Notes = "Kid vaccination at eight weeks."
            });
        await context.SaveChangesAsync();

        // The gestation screen's health-check table was empty. Give the confirmed pregnancy a
        // few month-by-month checks, and add a second, earlier pregnancy for variety.
        var karimGestation = await context.GestationRecords
            .Include(g => g.Animal)
            .FirstAsync(g => g.FarmId == farmId && g.Animal.TagNumber == "SR-1002");
        context.GestationHealthChecks.AddRange(
            new GestationHealthCheck
            {
                Id = Guid.NewGuid(), FarmId = farmId, GestationRecordId = karimGestation.Id,
                CheckDate = now.AddDays(-150), WeightKg = 455m, PerformedBy = aisha.Name,
                Notes = "Pregnancy confirmed by ultrasound at 60 days."
            },
            new GestationHealthCheck
            {
                Id = Guid.NewGuid(), FarmId = farmId, GestationRecordId = karimGestation.Id,
                CheckDate = now.AddDays(-90), WeightKg = 468m, PerformedBy = aisha.Name,
                Notes = "Fetal heartbeat strong; body condition good."
            },
            new GestationHealthCheck
            {
                Id = Guid.NewGuid(), FarmId = farmId, GestationRecordId = karimGestation.Id,
                CheckDate = now.AddDays(-30), WeightKg = 479m, PerformedBy = aisha.Name,
                Notes = "Late gestation, udder developing normally."
            });

        var sudan = await context.Animals.FirstAsync(a => a.TagNumber == "SR-1005");
        var secondBreeding = new BreedingRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId, SireId = sudan.Id, DamId = zayna.Id,
            BreedingDate = now.AddDays(-95), Method = BreedingMethod.AI,
            VetId = aisha.Id, VetName = aisha.Name, Result = BreedingResult.Confirmed,
            Notes = "AI with proven sire; confirmation at 60 days."
        };
        context.BreedingRecords.Add(secondBreeding);
        var secondGestationId = Guid.NewGuid();
        context.GestationRecords.Add(new GestationRecord
        {
            Id = secondGestationId, FarmId = farmId, BreedingRecordId = secondBreeding.Id,
            AnimalId = zayna.Id, ConfirmedDate = now.AddDays(-35),
            ExpectedDeliveryDate = now.AddDays(150), CurrentStage = GestationStage.Early,
            HealthCheckNotes = "Early pregnancy, progressing normally."
        });
        context.GestationHealthChecks.Add(new GestationHealthCheck
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            GestationRecordId = secondGestationId,
            CheckDate = now.AddDays(-35), WeightKg = 465m, PerformedBy = aisha.Name,
            Notes = "Confirmed by ultrasound."
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Two feed types and a goat ration turn the feed module from a single day's entries into a
    /// week of records with a visible cost trail and a mix of completed/pending feeding tasks.
    /// </summary>
    private static async Task SeedDemoFeedHistoryAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        var straw = await EnsureFeedTypeAsync(context, farmId, now,
            "Wheat Straw", FeedCategory.Forage, FeedUnit.Bale, 11m, "Baled straw, fed to the dry herd.");
        var goatPellets = await EnsureFeedTypeAsync(context, farmId, now,
            "Goat Grower Pellets", FeedCategory.Concentrate, FeedUnit.Kilogram, 680m, "Pelleted ration for the goat pen.");
        var lucerne = await context.FeedTypes.FirstAsync(f => f.FarmId == farmId && f.Name == "Lucerne Hay");
        var concentrate = await context.FeedTypes.FirstAsync(f => f.FarmId == farmId && f.Name == "Dairy Protein Concentrate");
        var calfPen = await context.Locations.FirstAsync(l => l.FarmId == farmId && l.Name == "Calf Pen");
        var zayna = await context.Animals.FirstAsync(a => a.TagNumber == "SR-1006");
        var sana = await context.Animals.FirstAsync(a => a.TagNumber == "BF-3001");
        var biscuit = await context.Animals.FirstAsync(a => a.TagNumber == "GT-2001");

        await EnsureFeedStockMovementAsync(context, farmId, now, straw.Id, StockMovementType.Purchase, 40, straw.CostPerUnit, "Sunrise Feeds Co.", now.AddDays(-25));
        await EnsureFeedStockMovementAsync(context, farmId, now, goatPellets.Id, StockMovementType.Purchase, 90, goatPellets.CostPerUnit, "AgriMinerals", now.AddDays(-12));
        await EnsureFeedStockMovementAsync(context, farmId, now, lucerne.Id, StockMovementType.Purchase, 20, lucerne.CostPerUnit, "Sunrise Feeds Co.", now.AddDays(-18));

        context.FeedRecords.AddRange(
            new FeedRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = straw.Id, AnimalId = sana.Id,
                Quantity = 4, UnitCost = straw.CostPerUnit, FedAt = now.AddDays(-6),
                MealSlot = MealSlot.Morning, MealRole = MealRole.Main,
                Notes = "Straw ration for the buffalo."
            },
            new FeedRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = goatPellets.Id, AnimalId = biscuit.Id,
                Quantity = 1.5m, UnitCost = goatPellets.CostPerUnit, FedAt = now.AddDays(-5),
                MealSlot = MealSlot.Morning, MealRole = MealRole.Main
            },
            new FeedRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = goatPellets.Id, LocationId = calfPen.Id,
                Quantity = 4m, UnitCost = goatPellets.CostPerUnit, FedAt = now.AddDays(-4),
                MealSlot = MealSlot.Evening, MealRole = MealRole.Supplement,
                Notes = "Evening ration for the whole goat pen."
            },
            new FeedRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = concentrate.Id, AnimalId = zayna.Id,
                Quantity = 5m, UnitCost = concentrate.CostPerUnit, FedAt = now.AddDays(-3),
                MealSlot = MealSlot.Morning, MealRole = MealRole.Main
            },
            new FeedRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = lucerne.Id, AnimalId = sana.Id,
                Quantity = 3m, UnitCost = lucerne.CostPerUnit, FedAt = now.AddDays(-2),
                MealSlot = MealSlot.Evening, MealRole = MealRole.Main
            },
            new FeedRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = straw.Id, LocationId = calfPen.Id,
                Quantity = 2m, UnitCost = straw.CostPerUnit, FedAt = now.AddDays(-1),
                MealSlot = MealSlot.Midday, MealRole = MealRole.Other
            });
        await context.SaveChangesAsync();

        var goatType = await context.AnimalTypes.FirstAsync(t => t.FarmId == farmId && t.Name == "Goat");
        var beetal = await context.Breeds.FirstAsync(b => b.AnimalTypeId == goatType.Id && b.Name == "Beetal");
        var goatPlan = await EnsureDietPlanAsync(context, farmId, now, goatType.Id, beetal.Id,
            "Goat Kid Ration", "Growing ration for the kids in the calf pen.",
            [(goatPellets.Id, 1.5m), (straw.Id, 2m)]);
        await EnsureFeedingScheduleAsync(context, farmId, now, goatPlan.Id, new TimeSpan(8, 0, 0), "Goat morning feed");

        // One completed and two pending/goat tasks across the last few days, so the feeding-task
        // board is not a single row. Dates are unique per schedule (the model enforces that).
        var morning = await context.FeedingSchedules.FirstAsync(s => s.DietPlanId != goatPlan.Id && s.Label == "Morning milking feed");
        var evening = await context.FeedingSchedules.FirstAsync(s => s.DietPlanId != goatPlan.Id && s.Label == "Evening feed");
        var goatMorning = await context.FeedingSchedules.FirstAsync(s => s.DietPlanId == goatPlan.Id && s.Label == "Goat morning feed");
        var milkPlanId = await context.FeedingSchedules.Where(s => s.Id == morning.Id).Select(s => s.DietPlanId).FirstAsync();

        context.FeedingTasks.AddRange(
            new FeedingTask
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedingScheduleId = morning.Id, DietPlanId = milkPlanId,
                TaskDate = now.AddDays(-4), TimeOfDay = TimeSpan.FromHours(6),
                Status = FeedingTaskStatus.Completed, CompletedAt = now.AddDays(-4).AddHours(6),
                TargetAnimalCount = 8, Notes = "Fed on time."
            },
            new FeedingTask
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedingScheduleId = evening.Id, DietPlanId = milkPlanId,
                TaskDate = now.AddDays(-2), TimeOfDay = new TimeSpan(17, 30, 0),
                Status = FeedingTaskStatus.Skipped, TargetAnimalCount = 8,
                Notes = "Skipped during the fence repair; hay was left in the paddock."
            },
            new FeedingTask
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedingScheduleId = goatMorning.Id, DietPlanId = goatPlan.Id,
                TaskDate = now.AddDays(-1), TimeOfDay = new TimeSpan(8, 0, 0),
                Status = FeedingTaskStatus.Completed, CompletedAt = now.AddDays(-1).AddHours(8),
                TargetAnimalCount = 3, Notes = "Kids fed and water refreshed."
            },
            new FeedingTask
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedingScheduleId = goatMorning.Id, DietPlanId = goatPlan.Id,
                TaskDate = now.Date, TimeOfDay = new TimeSpan(8, 0, 0),
                Status = FeedingTaskStatus.Pending, TargetAnimalCount = 3
            });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Extends inventory, purchasing and sales with a few more lines of stock history, then adds
    /// a six-month ledger so the finance charts and report filters have real month-to-month data.
    /// </summary>
    private static async Task SeedDemoTradeAndFinanceHistoryAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        var dairyCo = await EnsureSupplierAsync(context, farmId, now,
            "DairyCo Feeds", "orders@dairyco.example", "Concentrates and mineral supplements");
        var agriSupply = await EnsureSupplierAsync(context, farmId, now,
            "AgriSupply", "sales@agrisupply.example", "Vaccines, veterinary consumables and spares");
        var greenValley = await EnsureCustomerAsync(context, farmId, now,
            "Green Valley Dairy", "sales@greenvalley.example");
        var townMarket = await EnsureCustomerAsync(context, farmId, now,
            "Town Market Stall", "stall@townmarket.example");
        var organicCo = await EnsureCustomerAsync(context, farmId, now,
            "Organic Harvest Co.", "buyers@organicharvest.example");

        var strawBales = await EnsureInventoryItemAsync(context, farmId, now,
            "Wheat Straw Bales", "Forage", "Bale", 11m, 16, 20);
        var goatFeed = await EnsureInventoryItemAsync(context, farmId, now,
            "Goat Grower Pellets", "Feed", "Kg", 0.68m, 55, 30);
        var vetKit = await EnsureInventoryItemAsync(context, farmId, now,
            "Vet Consumables Kit", "Health", "Kit", 48m, 5, 4);
        var spareParts = await EnsureInventoryItemAsync(context, farmId, now,
            "Milking Machine Spares", "Equipment", "Set", 120m, 3, 2);

        await EnsureStockMovementAsync(context, farmId, now, strawBales.Id, InventoryMovementType.Purchase, 40, 11m, "Sunrise Feeds Co.");
        await EnsureStockMovementAsync(context, farmId, now, goatFeed.Id, InventoryMovementType.Purchase, 90, 0.68m, "DairyCo Feeds");
        await EnsureStockMovementAsync(context, farmId, now, vetKit.Id, InventoryMovementType.Purchase, 8, 48m, "AgriSupply");
        await EnsureStockMovementAsync(context, farmId, now, spareParts.Id, InventoryMovementType.Consumption, 1, 120m, null);

        await EnsureSupplierPurchaseAsync(context, farmId, now, dairyCo.Id, goatFeed.Id, 90, 612m, "Monthly concentrate order", now.AddDays(-12));
        await EnsureSupplierPurchaseAsync(context, farmId, now, agriSupply.Id, vetKit.Id, 8, 384m, "Veterinary consumables restock", now.AddDays(-21));
        await EnsureCustomerSaleAsync(context, farmId, now, townMarket.Id, strawBales.Id, 10, 145m, "Retail bale sales", now.AddDays(-8));
        await EnsureCustomerSaleAsync(context, farmId, now, organicCo.Id, goatFeed.Id, 20, 220m, "Goat feed resale", now.AddDays(-4));

        var feedCat = await context.ExpenseCategories.FirstAsync(c => c.FarmId == farmId && c.Name == "Feed");
        var healthCat = await context.ExpenseCategories.FirstAsync(c => c.FarmId == farmId && c.Name == "Veterinary");
        var maintCat = await context.ExpenseCategories.FirstAsync(c => c.FarmId == farmId && c.Name == "Maintenance");
        var labourCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Labour", "Wages and casual labour");
        var utilitiesCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Utilities", "Water, power and fuel");
        var milkCat = await context.IncomeCategories.FirstAsync(c => c.FarmId == farmId && c.Name == "Milk Sales");
        var livestockCat = await context.IncomeCategories.FirstAsync(c => c.FarmId == farmId && c.Name == "Livestock Sales");
        var produceCat = await EnsureIncomeCategoryAsync(context, farmId, now, "Farm Produce", "Straw, silage and minor produce");
        var cash = await context.PaymentMethods.FirstAsync(p => p.FarmId == farmId && p.Name == "Cash");
        var bank = await context.PaymentMethods.FirstAsync(p => p.FarmId == farmId && p.Name == "Bank Transfer");
        var mobile = await EnsurePaymentMethodAsync(context, farmId, now, "Mobile Money", "Mobile money transfers");

        // A fixed pattern over six months, keyed off the seed date so the rolling reports always
        // show a trend: milk income grows, feed and labour stay the biggest costs.
        for (var month = 1; month <= 6; month++)
        {
            var baseDate = now.AddMonths(-month);
            var when = new DateTime(baseDate.Year, baseDate.Month, 12);
            context.IncomeRecords.Add(new IncomeRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, IncomeDate = when,
                Amount = 1_180m + month * 55m, IncomeCategoryId = milkCat.Id,
                PaymentMethodId = bank.Id, Description = $"Milk sales — {when:MMMM yyyy}"
            });
            context.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = when.AddDays(2),
                Amount = 360m + month * 18m, ExpenseCategoryId = feedCat.Id,
                PaymentMethodId = bank.Id, Description = $"Feed and forage — {when:MMMM yyyy}"
            });
            context.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = when.AddDays(5),
                Amount = 2_850m, ExpenseCategoryId = labourCat.Id,
                PaymentMethodId = bank.Id, Description = $"Staff wages — {when:MMMM yyyy}"
            });
            context.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = when.AddDays(9),
                Amount = 95m + month * 7m, ExpenseCategoryId = utilitiesCat.Id,
                PaymentMethodId = mobile.Id, Description = $"Water and power — {when:MMMM yyyy}"
            });
        }

        context.IncomeRecords.AddRange(
            new IncomeRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, IncomeDate = now.AddMonths(-4).AddDays(18),
                Amount = 2_400m, IncomeCategoryId = livestockCat.Id, PaymentMethodId = bank.Id,
                Description = "Sale of a breeding heifer to a neighbouring farm"
            },
            new IncomeRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, IncomeDate = now.AddDays(-11),
                Amount = 620m, IncomeCategoryId = produceCat.Id, PaymentMethodId = cash.Id,
                Description = "Straw and silage sold to a neighbouring dairy"
            });
        context.Expenses.AddRange(
            new Expense
            {
                Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = now.AddDays(-20),
                Amount = 410m, ExpenseCategoryId = healthCat.Id, PaymentMethodId = cash.Id,
                Description = "Vaccines and veterinary consumables"
            },
            new Expense
            {
                Id = Guid.NewGuid(), FarmId = farmId, ExpenseDate = now.AddMonths(-2).AddDays(6),
                Amount = 780m, ExpenseCategoryId = maintCat.Id, PaymentMethodId = bank.Id,
                Description = "Milking parlour pump replacement"
            });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Assigns the seeded staff to their departments and roles, fills a fortnight of attendance,
    /// and gives the task board a realistic spread of owners, priorities and statuses.
    /// </summary>
    private static async Task SeedDemoStaffAndTaskHistoryAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        var vetRole = await context.EmployeeRoles.FirstAsync(r => r.FarmId == farmId && r.Name == "Veterinarian");
        var herdRole = await context.EmployeeRoles.FirstAsync(r => r.FarmId == farmId && r.Name == "Herdsman");
        var vetDept = await context.Departments.FirstAsync(d => d.FarmId == farmId && d.Name == "Veterinary");
        var opsDept = await context.Departments.FirstAsync(d => d.FarmId == farmId && d.Name == "Operations");

        var aishaEmployee = await context.Employees.FirstAsync(e => e.Email == "avet@sunriseranch.example");
        var marcus = await context.Employees.FirstAsync(e => e.Email == "mbell@sunriseranch.example");
        var liam = await context.Employees.FirstAsync(e => e.Email == "liam@sunriseranch.example");

        aishaEmployee.DepartmentId = vetDept.Id;
        aishaEmployee.EmployeeRoleId = vetRole.Id;
        marcus.DepartmentId = opsDept.Id;
        marcus.EmployeeRoleId = herdRole.Id;
        liam.DepartmentId = opsDept.Id;
        liam.EmployeeRoleId = herdRole.Id;

        var grace = await EnsureEmployeeAsync(context, farmId, now,
            "Grace Mwangi", "grace@sunriseranch.example", SalaryType.Monthly, 2_800m, now.AddYears(-1),
            opsDept, herdRole, "+1 555 0108");
        var noah = await EnsureEmployeeAsync(context, farmId, now,
            "Noah Rahman", "noah@sunriseranch.example", SalaryType.Daily, 95m, now.AddMonths(-7),
            vetDept, vetRole, "+1 555 0109");
        noah.IsActive = false; // left the team recently — History views still need his records
        noah.Notes = "Resigned; kept for historical attendance and payroll.";
        await context.SaveChangesAsync();

        // Fourteen weekdays of attendance with a deterministic pattern: mostly present, a late,
        // an absent, a half day and a leave — so the calendar has more than one colour.
        var statuses = new[] { AttendanceStatus.Present, AttendanceStatus.Present, AttendanceStatus.Present,
            AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Present, AttendanceStatus.Absent,
            AttendanceStatus.HalfDay, AttendanceStatus.Present, AttendanceStatus.Present,
            AttendanceStatus.Leave, AttendanceStatus.Present, AttendanceStatus.Present, AttendanceStatus.Present };
        var team = new[] { aishaEmployee, marcus, liam, grace };
        for (var i = 0; i < statuses.Length; i++)
        {
            var day = now.AddDays(-(i + 1));
            for (var e = 0; e < team.Length; e++)
            {
                // Stagger absences so no single day empties the farm.
                var status = statuses[(i + e * 3) % statuses.Length];
                await EnsureAttendanceAsync(context, farmId, now, team[e].Id, status, day);
            }
        }

        context.PerformanceReviews.AddRange(
            new PerformanceReview
            {
                Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = marcus.Id,
                Rating = 4, ReviewDate = now.AddMonths(-3),
                PeriodStart = now.AddMonths(-9), PeriodEnd = now.AddMonths(-3),
                Strengths = "Dependable herd manager; calving rates improved this year.",
                AreasForImprovement = "Delegate routine tasks more consistently.",
                ReviewedBy = aishaEmployee.Id
            },
            new PerformanceReview
            {
                Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = grace.Id,
                Rating = 4, ReviewDate = now.AddMonths(-1),
                PeriodStart = now.AddMonths(-7), PeriodEnd = now.AddMonths(-1),
                Strengths = "Careful record keeping and reliable feeding rounds.",
                AreasForImprovement = "Build confidence with the milking equipment checks.",
                ReviewedBy = marcus.Id
            });

        var period = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        foreach (var employee in new[] { aishaEmployee, marcus, liam, grace })
        {
            context.SalaryPayments.Add(new SalaryPayment
            {
                Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = employee.Id,
                Amount = employee.SalaryRate, PaymentDate = period.AddDays(25),
                SalaryType = employee.SalaryType, PaymentType = SalaryPaymentType.BankTransfer,
                PeriodCovered = period, Reference = $"SAL-{period:yyyyMM}",
                Notes = $"Monthly salary — {period:MMMM yyyy}."
            });
        }
        await context.SaveChangesAsync();

        var mainBarn = await context.Locations.FirstAsync(l => l.FarmId == farmId && l.Name == "Main Barn");
        var isolation = await context.Locations.FirstAsync(l => l.FarmId == farmId && l.Name == "Isolation Pen");
        var affected = await context.Animals.FirstAsync(a => a.TagNumber == "SR-1009");
        var overdue = new (string Title, string Description, FarmTaskPriority Priority, FarmTaskStatus Status,
            int DueOffset, Guid? EmployeeId, Guid? AnimalId, Guid? LocationId)[]
        {
            ("Check fencing along the east boundary", "Walk the full line after last week's storm and report any loose posts.", FarmTaskPriority.Medium, FarmTaskStatus.Pending, -6, liam.Id, null, null),
            ("Blood-test new arrivals before joining the herd", "Required before the two heifers move into the main barn.", FarmTaskPriority.High, FarmTaskStatus.Pending, -2, aishaEmployee.Id, null, null),
            ("Service the milking machine", "Monthly service: seals, vacuum pump and cleaning cycle.", FarmTaskPriority.Medium, FarmTaskStatus.InProgress, 2, marcus.Id, null, mainBarn.Id),
            ("Recheck Rania's respiratory treatment", "Vet to review temperature and appetite before ending isolation.", FarmTaskPriority.High, FarmTaskStatus.Pending, 1, aishaEmployee.Id, affected.Id, isolation.Id),
            ("Top up mineral blocks in all paddocks", "Blocks in the north paddock are almost finished.", FarmTaskPriority.Low, FarmTaskStatus.Pending, 5, grace.Id, null, null),
            ("Clean and bed the goat pen", "Weekly pen clean before the kids are weighed again.", FarmTaskPriority.Medium, FarmTaskStatus.Completed, -4, grace.Id, null, null),
            ("File quarterly vaccination records", "Compile the batch numbers and dates for the regulator.", FarmTaskPriority.Low, FarmTaskStatus.Completed, -8, marcus.Id, null, null),
            ("Replace broken water trough in north paddock", "Trough cracked overnight; temporary bucket in place.", FarmTaskPriority.High, FarmTaskStatus.Cancelled, -3, liam.Id, null, null)
        };
        context.FarmTasks.AddRange(overdue.Select(t => new FMS.Domain.Entities.FarmTask
        {
            Id = Guid.NewGuid(), FarmId = farmId, Title = t.Title, Description = t.Description,
            Priority = t.Priority, Status = t.Status, DueDate = now.AddDays(t.DueOffset),
            AssignedEmployeeId = t.EmployeeId, AnimalId = t.AnimalId, LocationId = t.LocationId,
            StartedAt = t.Status == FarmTaskStatus.InProgress || t.Status == FarmTaskStatus.Completed
                ? now.AddDays(-1) : null,
            CompletedAt = t.Status == FarmTaskStatus.Completed ? now.AddDays(-5) : null,
            CancelReason = t.Status == FarmTaskStatus.Cancelled ? "Replaced by a repair visit from the supplier." : null
        }));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The notification centre used to stay empty until the 15-minute dispatcher job ran. Seed a
    /// small set of the alerts the dashboard already computes for this farm, with the same
    /// source keys, so the list is populated on first login and the dispatcher refreshes the
    /// existing rows instead of creating duplicates.
    /// </summary>
    private static async Task SeedDemoNotificationsAsync(FmsDbContext context, Guid farmId, Guid userId, DateTime now)
    {
        var overdueTask = await context.FarmTasks
            .FirstAsync(t => t.FarmId == farmId && t.Status == FarmTaskStatus.Pending && t.DueDate < now);
        var gestation = await context.GestationRecords
            .FirstAsync(g => g.FarmId == farmId && g.ExpectedDeliveryDate > now && g.ExpectedDeliveryDate <= now.AddDays(14));
        var lowStock = await context.InventoryItems
            .FirstAsync(i => i.FarmId == farmId && i.Quantity < i.ReorderLevel);
        var expiredBatch = await context.MedicineStocks
            .FirstAsync(s => s.FarmId == farmId && s.ExpiryDate < now);
        var expiredMedicine = await context.Medicines
            .Where(m => m.Id == expiredBatch.MedicineId)
            .Select(m => new { m.Name, m.Unit, m.LowStockThreshold })
            .FirstAsync();
        var overdueAnimal = await context.Animals
            .FirstAsync(a => a.FarmId == farmId && a.TagNumber == "SR-1007");

        var notifications = new List<Notification>
        {
            new()
            {
                Id = Guid.NewGuid(), FarmId = farmId, UserId = userId,
                AlertType = NotificationAlertTypes.OverdueTask, Severity = NotificationSeverity.Critical,
                SourceKey = NotificationAlertTypes.SourceKeys.ForOverdueTask(overdueTask.Id),
                Title = $"Overdue: {overdueTask.Title}",
                Message = $"Task was due {overdueTask.DueDate:MMM dd, yyyy}",
                TitleKey = AlertMessageKeys.OverdueTaskTitle,
                TitleArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(("title", overdueTask.Title))),
                MessageKey = AlertMessageKeys.OverdueTaskMessage,
                MessageArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(("dueDate", overdueTask.DueDate))),
                Link = "/dashboard/tasks", DueDate = overdueTask.DueDate,
                CreatedAt = now.AddDays(-1)
            },
            new()
            {
                Id = Guid.NewGuid(), FarmId = farmId, UserId = userId,
                AlertType = NotificationAlertTypes.DueBirth, Severity = NotificationSeverity.Warning,
                SourceKey = NotificationAlertTypes.SourceKeys.ForDueBirth(gestation.Id),
                Title = "Birth due: SR-1002",
                Message = $"Expected {gestation.ExpectedDeliveryDate:MMM dd, yyyy} ({(gestation.ExpectedDeliveryDate.Date - now.Date).Days} days). Stage: {gestation.CurrentStage}.",
                TitleKey = AlertMessageKeys.DueBirthTitle,
                TitleArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(("tag", "SR-1002"))),
                MessageKey = AlertMessageKeys.DueBirthMessage,
                MessageArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(
                    ("expectedDate", gestation.ExpectedDeliveryDate),
                    ("days", (gestation.ExpectedDeliveryDate.Date - now.Date).Days),
                    ("stage", gestation.CurrentStage.ToString()))),
                Link = "/dashboard/breeding/gestation", DueDate = gestation.ExpectedDeliveryDate,
                CreatedAt = now.AddDays(-2), ReadAtUtc = now.AddDays(-1)
            },
            new()
            {
                Id = Guid.NewGuid(), FarmId = farmId, UserId = userId,
                AlertType = NotificationAlertTypes.LowInventory, Severity = NotificationSeverity.Warning,
                SourceKey = NotificationAlertTypes.SourceKeys.ForLowInventory(lowStock.Id),
                Title = $"Low stock: {lowStock.Name}",
                Message = $"{lowStock.Quantity} {lowStock.Unit} remaining (reorder level: {lowStock.ReorderLevel}).",
                TitleKey = AlertMessageKeys.LowInventoryTitle,
                TitleArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(("item", lowStock.Name))),
                MessageKey = AlertMessageKeys.LowInventoryMessage,
                MessageArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(
                    ("quantity", lowStock.Quantity), ("unit", lowStock.Unit),
                    ("reorderLevel", lowStock.ReorderLevel),
                    ("shortfall", lowStock.ReorderLevel - lowStock.Quantity))),
                Link = "/dashboard/inventory/reports", CreatedAt = now.AddDays(-3)
            },
            new()
            {
                Id = Guid.NewGuid(), FarmId = farmId, UserId = userId,
                AlertType = NotificationAlertTypes.Medicine, Severity = NotificationSeverity.Critical,
                SourceKey = NotificationAlertTypes.SourceKeys.ForMedicine(
                    expiredBatch.MedicineId, expiredBatch.Id, "Expired"),
                Title = $"Expired: {expiredMedicine.Name}",
                Message = $"Batch {expiredBatch.BatchNumber} expired on {expiredBatch.ExpiryDate:MMM dd, yyyy}.",
                TitleKey = AlertMessageKeys.MedicineExpiredTitle,
                TitleArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(("medicine", expiredMedicine.Name))),
                MessageKey = AlertMessageKeys.MedicineMessage,
                MessageArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(
                    ("batch", expiredBatch.BatchNumber),
                    ("quantity", expiredBatch.Quantity),
                    ("unit", expiredMedicine.Unit),
                    ("threshold", expiredMedicine.LowStockThreshold),
                    ("expiryDate", expiredBatch.ExpiryDate))),
                Link = "/dashboard/health/medicines/alerts", DueDate = expiredBatch.ExpiryDate,
                CreatedAt = now.AddDays(-4), DismissedAtUtc = now.AddDays(-3)
            },
            new()
            {
                Id = Guid.NewGuid(), FarmId = farmId, UserId = userId,
                AlertType = NotificationAlertTypes.OverdueVaccination, Severity = NotificationSeverity.Critical,
                SourceKey = NotificationAlertTypes.SourceKeys.ForOverdueVaccination(
                    overdueAnimal.Id, await context.VaccineTypes
                        .Where(v => v.FarmId == farmId && v.Name == "Foot and Mouth Disease Vaccine")
                        .Select(v => v.Id).FirstAsync()),
                Title = "Overdue: Foot and Mouth Disease Vaccine",
                Message = $"Animal {overdueAnimal.TagNumber} is due for Foot and Mouth Disease Vaccine.",
                TitleKey = AlertMessageKeys.OverdueVaccinationTitle,
                TitleArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(
                    ("vaccineType", "Foot and Mouth Disease Vaccine"))),
                Link = "/dashboard/health/vaccinations", DueDate = now,
                CreatedAt = now.AddDays(-1)
            }
        };
        context.Notifications.AddRange(notifications);

        // A few explicit preferences so the preferences page is not empty. The demo never sends
        // email or push (DemoGuards blocks the real-email paths), so out-of-app channels stay off.
        foreach (var alertType in new[]
                 {
                     NotificationAlertTypes.OverdueTask,
                     NotificationAlertTypes.DueBirth,
                     NotificationAlertTypes.Medicine,
                     NotificationAlertTypes.LowInventory
                 })
        {
            context.NotificationPreferences.Add(new NotificationPreference
            {
                Id = Guid.NewGuid(), FarmId = farmId, UserId = userId,
                AlertType = alertType, InAppEnabled = true,
                EmailEnabled = false, PushEnabled = false,
                CreatedAt = now.AddMonths(-1)
            });
        }
        await context.SaveChangesAsync();
    }

    private static Guid? AgeCategoryFor(IReadOnlyDictionary<string, AgeCategory> categories, int ageDays)
    {
        var name = ageDays <= 180 ? "Calf" : ageDays <= 365 ? "Young" : "Adult";
        return categories.TryGetValue(name, out var category) ? category.Id : null;
    }

    private static async Task<Dictionary<string, Location>> EnsureDemoLocationsAsync(
        FmsDbContext context, Guid farmId, DateTime now)
    {
        var types = await context.LocationTypes
            .Where(t => t.FarmId == farmId)
            .ToDictionaryAsync(t => t.Name);

        var specs = new (string Name, string Type)[]
        {
            ("Main Barn", "Barn"),
            ("North Pasture", "Field"),
            ("South Paddock", "Paddock"),
            ("Maternity Pen", "Pen"),
            ("Poultry House", "Shed"),
        };

        var result = await context.Locations
            .Where(l => l.FarmId == farmId)
            .ToDictionaryAsync(l => l.Name);

        foreach (var spec in specs)
        {
            if (result.ContainsKey(spec.Name))
                continue;
            var location = new Location
            {
                Id = Guid.NewGuid(), FarmId = farmId, Name = spec.Name,
                LocationTypeId = types[spec.Type].Id,
                CreatedAt = now,
            };
            context.Locations.Add(location);
            result[spec.Name] = location;
        }

        await context.SaveChangesAsync();
        return result;
    }

    private static async Task AddStockMovementAtAsync(FmsDbContext context, Guid farmId, Guid inventoryItemId,
        InventoryMovementType movementType, decimal quantity, DateTime movementDate, string? reason)
    {
        context.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(), FarmId = farmId, InventoryItemId = inventoryItemId,
            MovementType = movementType, Quantity = quantity,
            MovementDate = movementDate, Reason = reason,
        });
        await context.SaveChangesAsync();
    }
}
