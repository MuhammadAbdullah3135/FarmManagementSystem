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
    private static async Task SeedEverythingAsync(FmsDbContext context, Guid farmId, DateTime now)
    {
        // ── Staff ────────────────────────────────────────────────────────────────────────
        var vet = await EnsureEmployeeAsync(context, farmId, now,
            "Dr. Aisha Karim", "avet@sunriseranch.example",
            SalaryType.Monthly, 9_500m, now.AddYears(-3));
        var manager = await EnsureEmployeeAsync(context, farmId, now,
            "Marcus Bell", "mbell@sunriseranch.example",
            SalaryType.Monthly, 6_200m, now.AddYears(-4));
        var worker = await EnsureEmployeeAsync(context, farmId, now,
            "Liam O'Connor", "liam@sunriseranch.example",
            SalaryType.Monthly, 3_100m, now.AddMonths(-1));
        var employees = new[] { vet, manager, worker };

        var veterinary = await EnsureDepartmentAsync(context, farmId, now, "Veterinary");
        var operations = await EnsureDepartmentAsync(context, farmId, now, "Operations");
        var vetRole = await EnsureEmployeeRoleAsync(context, farmId, now, "Veterinarian", "On-farm vet");
        var herdsmanRole = await EnsureEmployeeRoleAsync(context, farmId, now, "Herdsman", "Animal handling and feeding");
        var managerRole = await EnsureEmployeeRoleAsync(context, farmId, now, "Farm Manager", "Day-to-day operations");

        vet.DepartmentId = veterinary.Id;
        vet.EmployeeRoleId = vetRole.Id;
        manager.DepartmentId = operations.Id;
        manager.EmployeeRoleId = managerRole.Id;
        worker.DepartmentId = operations.Id;
        worker.EmployeeRoleId = herdsmanRole.Id;
        await context.SaveChangesAsync();

        // ── Finance setup ─────────────────────────────────────────────────────────────────
        var feedCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Feed", "Feed and foraging costs");
        var healthCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Veterinary", "Medical and vaccine costs");
        var maintCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Maintenance", "Repairs and upkeep");
        var labourCat = await EnsureExpenseCategoryAsync(context, farmId, now, "Labour", "Wages and seasonal labour");
        var milkCat = await EnsureIncomeCategoryAsync(context, farmId, now, "Milk Sales", "Fresh milk sales");
        var livestockCat = await EnsureIncomeCategoryAsync(context, farmId, now, "Livestock Sales", "Animal sales");
        var poultryCat = await EnsureIncomeCategoryAsync(context, farmId, now, "Poultry Sales", "Egg and poultry sales");
        var cash = await EnsurePaymentMethodAsync(context, farmId, now, "Cash", "Cash payments");
        var bank = await EnsurePaymentMethodAsync(context, farmId, now, "Bank Transfer", "Bank transfers");
        var mobile = await EnsurePaymentMethodAsync(context, farmId, now, "Mobile Money", "Mobile wallet payments");

        // ── Locations ─────────────────────────────────────────────────────────────────────
        var locations = await EnsureDemoLocationsAsync(context, farmId, now);
        var mainBarn = locations["Main Barn"];
        var northPasture = locations["North Pasture"];
        var southPaddock = locations["South Paddock"];
        var maternityPen = locations["Maternity Pen"];
        var poultryHouse = locations["Poultry House"];

        // ── Lookups ───────────────────────────────────────────────────────────────────────
        var animalTypes = await context.AnimalTypes
            .Include(t => t.Breeds)
            .Where(t => t.FarmId == farmId)
            .ToDictionaryAsync(t => t.Name);
        var statuses = await context.AnimalStatuses
            .Where(s => s.FarmId == farmId)
            .ToDictionaryAsync(s => s.Name);
        var sickStatus = await EnsureAnimalStatusAsync(context, farmId, "Sick", now);
        statuses["Sick"] = sickStatus;
        var sexes = await context.SexOptions
            .Where(s => s.FarmId == farmId)
            .ToDictionaryAsync(s => s.Value);
        var ageCategories = await context.AgeCategories
            .Where(c => c.FarmId == farmId)
            .ToDictionaryAsync(c => c.Name);

        // ── Animals: 30 head across five species, with every status represented ───────────
        var animalSpecs = new (string Tag, string? Name, string Type, string Breed, string Sex,
            string Status, int AgeDays, string Location, string? Notes)[]
        {
            ("SR-1001", "Bella", "Cattle", "Sahiwal", "Female", "Lactating", 1120, "Main Barn", "Star cow, top milk yield."),
            ("SR-1002", "Karima", "Cattle", "Sahiwal", "Female", "Pregnant", 1000, "Maternity Pen", "Confirmed pregnancy, due soon."),
            ("SR-1003", "Nasra", "Cattle", "Sahiwal", "Female", "Lactating", 1460, "North Pasture", "Older cow, steady producer."),
            ("SR-1004", "Duna", "Cattle", "Sahiwal", "Female", "Dry", 600, "South Paddock", "Dry period after weaning."),
            ("SR-1005", "Sudan", "Cattle", "Sahiwal", "Male", "Active", 730, "North Pasture", "Breeding bull."),
            ("SR-1006", "Farah", "Cattle", "Holstein Friesian", "Female", "Lactating", 1250, "Main Barn", "High-yield Holstein."),
            ("SR-1007", "Gabi", "Cattle", "Holstein Friesian", "Female", "Pregnant", 980, "Maternity Pen", "Second-lactation cow."),
            ("SR-1008", "Hala", "Cattle", "Sahiwal", "Female", "Sick", 1100, "Main Barn", "On treatment for foot infection."),
            ("SR-1009", "Idris", "Cattle", "Sahiwal", "Male", "Active", 850, "South Paddock", "Young breeding bull."),
            ("SR-1010", "Jamila", "Cattle", "Sahiwal", "Female", "Dry", 1600, "South Paddock", "Retired from milk production."),
            ("SR-1011", "Kito", "Cattle", "Holstein Friesian", "Male", "Sold", 900, "Main Barn", "Sold at auction."),
            ("SR-1012", "Layla", "Cattle", "Sahiwal", "Female", "Deceased", 1400, "Main Barn", "Euthanised after chronic illness."),
            ("SR-2001", "Muna", "Buffalo", "Murrah", "Female", "Lactating", 1050, "North Pasture", "Buffalo milk for cheese."),
            ("SR-2002", "Nia", "Buffalo", "Murrah", "Female", "Pregnant", 980, "Maternity Pen", "Confirmed pregnancy."),
            ("SR-2003", "Oba", "Buffalo", "Murrah", "Male", "Active", 760, "South Paddock", "Buffalo breeding bull."),
            ("SR-2004", "Pili", "Buffalo", "Murrah", "Female", "Dry", 1300, "South Paddock", "Dry buffalo cow."),
            ("SR-2005", "Rafa", "Buffalo", "Murrah", "Female", "Sold", 1100, "Main Barn", "Sold to a neighbouring farm."),
            ("SR-3001", "Sana", "Goat", "Beetal", "Female", "Lactating", 420, "North Pasture", "Goat milk for the household."),
            ("SR-3002", "Tariq", "Goat", "Beetal", "Male", "Active", 390, "North Pasture", "Breeding buck."),
            ("SR-3003", "Umi", "Goat", "Boer", "Female", "Pregnant", 460, "Maternity Pen", "Boer doe, confirmed pregnancy."),
            ("SR-3004", "Vito", "Goat", "Boer", "Male", "Sick", 330, "Main Barn", "Respiratory infection."),
            ("SR-3005", "Wafa", "Goat", "Beetal", "Female", "Active", 500, "South Paddock", "Healthy doe."),
            ("SR-4001", "Xena", "Sheep", "Kajli", "Female", "Lactating", 380, "North Pasture", "Ewe with a young lamb."),
            ("SR-4002", "Yara", "Sheep", "Kajli", "Female", "Active", 450, "South Paddock", "Mature ewe."),
            ("SR-4003", "Zaid", "Sheep", "Kajli", "Male", "Sold", 410, "Main Barn", "Sold for breeding."),
            ("SR-4004", "Amani", "Sheep", "Kajli", "Female", "Deceased", 520, "Main Barn", "Predator injury; euthanised."),
            ("SR-5001", "Coco", "Poultry", "Rhode Island Red", "Female", "Active", 220, "Poultry House", "Layer hen."),
            ("SR-5002", "Daisy", "Poultry", "Rhode Island Red", "Female", "Active", 210, "Poultry House", "Layer hen."),
            ("SR-5003", "Echo", "Poultry", "Rhode Island Red", "Male", "Active", 240, "Poultry House", "Breeding rooster."),
            ("SR-5004", "Fifi", "Poultry", "Rhode Island Red", "Female", "Sick", 200, "Poultry House", "Respiratory infection."),
        };

        var animals = new List<Animal>();
        foreach (var spec in animalSpecs)
        {
            var type = animalTypes[spec.Type];
            var breed = type.Breeds.FirstOrDefault(b => b.Name == spec.Breed)
                ?? throw new InvalidOperationException($"{spec.Breed} breed missing for {spec.Type}.");
            var location = locations[spec.Location];
            animals.Add(new Animal
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                TagNumber = spec.Tag,
                Name = spec.Name,
                AnimalTypeId = type.Id,
                BreedId = breed.Id,
                SexOptionId = sexes[spec.Sex].Id,
                AgeCategoryId = AgeCategoryFor(ageCategories, spec.AgeDays),
                AnimalStatusId = statuses[spec.Status].Id,
                LocationId = location.Id,
                DateOfBirth = now.AddDays(-spec.AgeDays),
                AcquisitionDate = now.AddDays(-spec.AgeDays + 30),
                Notes = spec.Notes,
            });
        }
        context.Animals.AddRange(animals);
        await context.SaveChangesAsync();

        var byTag = animals.ToDictionary(a => a.TagNumber);

        // ── Weights: two recent measurements for every animal ────────────────────────────
        for (var i = 0; i < animals.Count; i++)
        {
            var animal = animals[i];
            var typeName = animalSpecs[i].Type;
            var baseWeight = typeName switch
            {
                "Cattle" => 430m,
                "Buffalo" => 520m,
                "Goat" => 42m,
                "Sheep" => 38m,
                "Poultry" => 2.4m,
                _ => 100m,
            };

            for (var j = 0; j < 2; j++)
            {
                var delta = ((i * 7 + j * 11) % 25) - 12;
                var weight = Math.Max(0.8m, baseWeight + delta - (j * 7));
                context.WeightRecords.Add(new WeightRecord
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmId,
                    AnimalId = animal.Id,
                    WeightKg = weight,
                    RecordedAt = now.AddDays(-(8 + ((i * 5 + j * 31) % 120))),
                    Notes = j == 0 ? "Routine weighing." : "Previous weight check.",
                });
            }
        }
        await context.SaveChangesAsync();

        // ── Breeding: four current pregnancies, four past births, two other outcomes ─────
        var currentPregnancies = new (string Dam, string Sire, int BredDaysAgo, int DueInDays,
            BreedingMethod Method, GestationStage Stage)[]
        {
            ("SR-1002", "SR-1005", 200, 10, BreedingMethod.Natural, GestationStage.Late),
            ("SR-1007", "SR-1009", 180, 25, BreedingMethod.AI, GestationStage.Mid),
            ("SR-2002", "SR-2003", 160, 40, BreedingMethod.Natural, GestationStage.Mid),
            ("SR-3003", "SR-3002", 120, 60, BreedingMethod.Natural, GestationStage.Early),
        };

        var gestationRecords = new List<GestationRecord>();
        foreach (var p in currentPregnancies)
        {
            var breeding = new BreedingRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                SireId = byTag[p.Sire].Id,
                DamId = byTag[p.Dam].Id,
                BreedingDate = now.AddDays(-p.BredDaysAgo),
                Method = p.Method,
                VetName = FullName(vet),
                Result = BreedingResult.Confirmed,
                Notes = "Pregnancy confirmed by the farm vet.",
            };
            context.BreedingRecords.Add(breeding);

            var gestation = new GestationRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                BreedingRecordId = breeding.Id,
                AnimalId = byTag[p.Dam].Id,
                ConfirmedDate = now.AddDays(-p.BredDaysAgo + 25),
                ExpectedDeliveryDate = now.AddDays(p.DueInDays),
                CurrentStage = p.Stage,
                HealthCheckNotes = "Healthy progression, no concerns.",
            };
            context.GestationRecords.Add(gestation);
            gestationRecords.Add(gestation);
        }

        var pastBirths = new (string Dam, string Sire, int BredDaysAgo, int BirthDaysAgo)[]
        {
            ("SR-1001", "SR-1005", 340, 60),
            ("SR-1003", "SR-1005", 380, 90),
            ("SR-1006", "SR-1009", 320, 120),
            ("SR-2001", "SR-2003", 360, 45),
        };

        var birthRecords = new List<BirthRecord>();
        foreach (var b in pastBirths)
        {
            var breeding = new BreedingRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                SireId = byTag[b.Sire].Id,
                DamId = byTag[b.Dam].Id,
                BreedingDate = now.AddDays(-b.BredDaysAgo),
                Method = BreedingMethod.Natural,
                VetName = FullName(vet),
                Result = BreedingResult.Confirmed,
                Notes = "Completed pregnancy resulting in a live birth.",
            };
            context.BreedingRecords.Add(breeding);

            var birth = new BirthRecord
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                DamId = byTag[b.Dam].Id,
                BreedingRecordId = breeding.Id,
                BirthDate = now.AddDays(-b.BirthDaysAgo),
                OffspringCount = 2,
                VetName = FullName(vet),
                Notes = "Twins; both recorded at birth.",
            };
            context.BirthRecords.Add(birth);
            birthRecords.Add(birth);
        }

        // Two more breeding outcomes: a failed service and a pending service.
        context.BreedingRecords.Add(new BreedingRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            SireId = byTag["SR-1005"].Id, DamId = byTag["SR-1004"].Id,
            BreedingDate = now.AddDays(-60), Method = BreedingMethod.Natural,
            Result = BreedingResult.Failed, VetName = FullName(vet),
            Notes = "Returned to heat after 21 days.",
        });
        context.BreedingRecords.Add(new BreedingRecord
        {
            Id = Guid.NewGuid(), FarmId = farmId,
            SireId = byTag["SR-3002"].Id, DamId = byTag["SR-3001"].Id,
            BreedingDate = now.AddDays(-20), Method = BreedingMethod.Natural,
            Result = BreedingResult.Pending, VetName = FullName(vet),
            Notes = "Observation pending; recheck next cycle.",
        });

        // Eight gestation checks, two per current pregnancy.
        var gestationCheckNotes = new[]
        {
            "Body condition stable; continue current ration.",
            "No discharge or swelling; pregnancy progressing normally.",
        };
        for (var i = 0; i < gestationRecords.Count; i++)
        {
            for (var j = 0; j < 2; j++)
            {
                context.GestationHealthChecks.Add(new GestationHealthCheck
                {
                    Id = Guid.NewGuid(), FarmId = farmId,
                    GestationRecordId = gestationRecords[i].Id,
                    CheckDate = now.AddDays(-(20 + i * 5 + j * 30)),
                    PerformedBy = FullName(vet),
                    WeightKg = 420 + i * 18 - j * 5,
                    Notes = gestationCheckNotes[j],
                });
            }
        }

        // Eight offspring across the four births.
        for (var i = 0; i < birthRecords.Count; i++)
        {
            var birth = birthRecords[i];
            var damTag = animalSpecs.First(a => a.Tag == byTag.First(kv => kv.Value.Id == birth.DamId).Key).Tag;
            context.BirthOffspring.AddRange(
                new BirthOffspring
                {
                    Id = Guid.NewGuid(), FarmId = farmId, BirthRecordId = birth.Id,
                    TagNumber = $"{damTag}-A", SexOptionId = sexes["Female"].Id,
                    Outcome = BirthOutcome.Alive, BirthWeightKg = 32 + i,
                    Notes = "Healthy female calf, kept as a replacement.",
                },
                new BirthOffspring
                {
                    Id = Guid.NewGuid(), FarmId = farmId, BirthRecordId = birth.Id,
                    TagNumber = $"{damTag}-B", SexOptionId = sexes[i % 2 == 0 ? "Male" : "Female"].Id,
                    Outcome = i == 2 ? BirthOutcome.Weak : BirthOutcome.Alive,
                    BirthWeightKg = 30 + i,
                    Notes = i == 2 ? "Weak at birth; monitored closely." : "Healthy offspring.",
                });
        }
        await context.SaveChangesAsync();

        // ── Health: medicines, batches, vaccines, records, schedules ─────────────────────
        var oxy = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Oxytetracycline", "ml", 50, 30, 100, 2.50m, "BATCH-OXY-001", now.AddDays(60), now.AddDays(-30));
        var mel = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Meloxicam", "ml", 30, 21, 60, 1.80m, "BATCH-MEL-001", now.AddDays(90), now.AddDays(-10));
        var ivermectin = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Ivermectin", "ml", 20, 30, 40, 1.20m, "BATCH-IVE-001", now.AddDays(120), now.AddDays(-45));
        var penicillin = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Penicillin", "ml", 25, 30, 80, 0.90m, "BATCH-PEN-001", now.AddDays(75), now.AddDays(-20));
        var tylosin = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Tylosin", "ml", 15, 45, 25, 2.10m, "BATCH-TYL-001", now.AddDays(100), now.AddDays(-15));
        var vitamins = await EnsureMedicineWithStockAsync(context, farmId, now,
            "Multivitamin Injection", "ml", 10, 60, 30, 1.40m, "BATCH-VIT-001", now.AddDays(150), now.AddDays(-5));

        // Four more batches so the stock screen has a realistic spread (10 total).
        context.MedicineStocks.AddRange(
            new MedicineStock
            {
                Id = Guid.NewGuid(), FarmId = farmId, MedicineId = oxy.Id,
                BatchNumber = "BATCH-OXY-002", Quantity = 8, UnitCost = 2.50m,
                ExpiryDate = now.AddDays(-5), DateReceived = now.AddMonths(-4),
                Supplier = "Sunrise Feeds Co.",
            },
            new MedicineStock
            {
                Id = Guid.NewGuid(), FarmId = farmId, MedicineId = mel.Id,
                BatchNumber = "BATCH-MEL-002", Quantity = 12, UnitCost = 1.80m,
                ExpiryDate = now.AddDays(45), DateReceived = now.AddDays(-60),
                Supplier = "Veterinary Wholesale",
            },
            new MedicineStock
            {
                Id = Guid.NewGuid(), FarmId = farmId, MedicineId = ivermectin.Id,
                BatchNumber = "BATCH-IVE-002", Quantity = 5, UnitCost = 1.20m,
                ExpiryDate = now.AddDays(-10), DateReceived = now.AddDays(-90),
                Supplier = "AgriSupply",
            },
            new MedicineStock
            {
                Id = Guid.NewGuid(), FarmId = farmId, MedicineId = penicillin.Id,
                BatchNumber = "BATCH-PEN-002", Quantity = 9, UnitCost = 0.90m,
                ExpiryDate = now.AddDays(15), DateReceived = now.AddDays(-70),
                Supplier = "Veterinary Wholesale",
            });
        await context.SaveChangesAsync();

        var fmdVax = await EnsureVaccineTypeAsync(context, farmId, now,
            "Foot and Mouth Disease Vaccine", "5ml", oxy.Id);
        var anthraxVax = await EnsureVaccineTypeAsync(context, farmId, now,
            "Anthrax Vaccine", "2ml", penicillin.Id);
        var rabiesVax = await EnsureVaccineTypeAsync(context, farmId, now,
            "Rabies Vaccine", "1ml", ivermectin.Id);
        var newcastleVax = await EnsureVaccineTypeAsync(context, farmId, now,
            "Newcastle Disease Vaccine", "0.2ml", vitamins.Id);
        var vaccineTypes = new[] { fmdVax, anthraxVax, rabiesVax, newcastleVax };

        var medicalRecords = new (string Tag, string Symptoms, string Diagnosis, string Treatment,
            string Medicine, string Dosage, MedicalRecordStatus Status, int DateDaysAgo,
            int? FollowUpDays, decimal Cost, string? Notes)[]
        {
            ("SR-1008", "Limping on front left leg, swelling above hoof.", "Foot infection (foot rot)",
                "Oxytetracycline spray + systemic antibiotics", "Oxytetracycline", "10ml IM daily x 5 days",
                MedicalRecordStatus.Open, 7, 5, 75m, "Improving; recheck in 5 days."),
            ("SR-3004", "Coughing, nasal discharge, reduced appetite.", "Respiratory infection",
                "Tylosin injection + supportive care", "Tylosin", "5ml IM daily x 3 days",
                MedicalRecordStatus.InProgress, 5, 2, 42m, "Isolated in the main barn."),
            ("SR-5004", "Laboured breathing, ruffled feathers.", "Poultry respiratory infection",
                "Multivitamin + supportive care", "Multivitamin Injection", "0.2ml SC once",
                MedicalRecordStatus.Open, 3, 4, 8m, "Monitor the flock for spread."),
            ("SR-1012", "Progressive weight loss and weakness.", "Chronic wasting disease",
                "Palliative care; euthanasia recommended", "Meloxicam", "15ml IM once",
                MedicalRecordStatus.Resolved, 30, null, 120m, "Euthanised; resolved record retained."),
            ("SR-4004", "Deep laceration from a predator.", "Predator injury",
                "Wound repair; euthanasia due to severity", "Oxytetracycline", "10ml IM once",
                MedicalRecordStatus.Resolved, 45, null, 95m, "Euthanised; resolved record retained."),
            ("SR-1001", "Swollen quarter, reduced milk.", "Clinical mastitis",
                "Intramammary antibiotic + meloxicam", "Meloxicam", "15ml IM + tube",
                MedicalRecordStatus.Resolved, 25, null, 55m, "Milk withheld for three days."),
            ("SR-1003", "Wound on flank from barbed wire.", "Superficial wound",
                "Clean, topical antibiotic, tetanus booster", "Oxytetracycline", "Topical spray daily",
                MedicalRecordStatus.Resolved, 40, null, 18m, "Healed without complications."),
            ("SR-1002", "Routine pregnancy check.", "Confirmed pregnancy",
                "No treatment required", "Multivitamin Injection", "10ml IM once",
                MedicalRecordStatus.Resolved, 100, null, 12m, "Healthy pregnancy."),
            ("SR-1007", "Mild lameness in right hind leg.", "Lameness",
                "Hoof trim + anti-inflammatory", "Meloxicam", "15ml IM once",
                MedicalRecordStatus.InProgress, 6, 3, 38m, "Hoof trimmed; recheck gait."),
            ("SR-2002", "Staggering and cold ears after calving.", "Milk fever",
                "Calcium borogluconate", "Multivitamin Injection", "500ml IV",
                MedicalRecordStatus.Resolved, 20, null, 65m, "Recovered within 24 hours."),
            ("SR-3003", "Weight loss and rough coat.", "Internal parasites",
                "Deworming", "Ivermectin", "2ml SC once",
                MedicalRecordStatus.Resolved, 15, null, 14m, "Repeat faecal check in one month."),
            ("SR-1004", "Overgrown hoof.", "Hoof overgrowth",
                "Corrective trim", "Meloxicam", "10ml IM once",
                MedicalRecordStatus.Resolved, 50, null, 25m, "Normal gait after trim."),
            ("SR-1006", "Hot, swollen udder.", "Clinical mastitis",
                "Intramammary antibiotic + meloxicam", "Meloxicam", "15ml IM + tube",
                MedicalRecordStatus.Resolved, 35, null, 58m, "Milk culture negative on recheck."),
            ("SR-2001", "Minor teat injury.", "Teat laceration",
                "Clean and topical antibiotic", "Oxytetracycline", "Topical daily x 3 days",
                MedicalRecordStatus.Resolved, 60, null, 22m, "Healed; no effect on yield."),
        };

        foreach (var m in medicalRecords)
        {
            context.MedicalRecords.Add(new MedicalRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = byTag[m.Tag].Id,
                Symptoms = m.Symptoms, Diagnosis = m.Diagnosis, Treatment = m.Treatment,
                MedicineUsed = m.Medicine, Dosage = m.Dosage, VetName = FullName(vet),
                Cost = m.Cost, DateRecorded = now.AddDays(-m.DateDaysAgo),
                FollowUpDate = m.FollowUpDays.HasValue ? now.AddDays(m.FollowUpDays.Value) : null,
                Status = m.Status, Notes = m.Notes,
            });
        }
        await context.SaveChangesAsync();

        var batchNumbers = new[]
        {
            "BATCH-OXY-001", "BATCH-MEL-001", "BATCH-IVE-001",
            "BATCH-PEN-001", "BATCH-TYL-001", "BATCH-VIT-001",
        };
        for (var i = 0; i < 18; i++)
        {
            var animal = animals[(i * 7) % animals.Count];
            var vaccine = vaccineTypes[i % vaccineTypes.Length];
            context.VaccinationRecords.Add(new VaccinationRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalId = animal.Id,
                VaccineTypeId = vaccine.Id, DateGiven = now.AddDays(-(20 + i * 7)),
                VetName = FullName(vet), BatchNumber = batchNumbers[i % batchNumbers.Length],
                Cost = 8m + (i % 5), Notes = i % 3 == 0 ? "Routine preventative booster." : null,
            });
        }

        var cattleType = animalTypes["Cattle"];
        var buffaloType = animalTypes["Buffalo"];
        var goatType = animalTypes["Goat"];
        var sheepType = animalTypes["Sheep"];
        var poultryType = animalTypes["Poultry"];

        var vaccinationSchedules = new (VaccineType Vaccine, AnimalType Type, int Days, string Notes)[]
        {
            (fmdVax, cattleType, 180, "Six-monthly FMD booster for cattle."),
            (anthraxVax, cattleType, 365, "Annual anthrax vaccination."),
            (rabiesVax, sheepType, 365, "Annual rabies vaccination for sheep."),
            (newcastleVax, poultryType, 90, "Quarterly Newcastle booster for poultry."),
            (fmdVax, buffaloType, 180, "Six-monthly FMD booster for buffalo."),
        };
        foreach (var s in vaccinationSchedules)
        {
            context.VaccinationSchedules.Add(new VaccinationSchedule
            {
                Id = Guid.NewGuid(), FarmId = farmId, VaccineTypeId = s.Vaccine.Id,
                AnimalTypeId = s.Type.Id, RecurrenceDays = s.Days,
                IsActive = true, Notes = s.Notes, CreatedAt = now.AddMonths(-6),
            });
        }

        var weightSchedules = new (AnimalType Type, int Days, string Notes)[]
        {
            (cattleType, 60, "Monthly weight check for cattle."),
            (goatType, 45, "Six-weekly weight check for goats."),
            (poultryType, 30, "Monthly weight check for poultry."),
        };
        foreach (var s in weightSchedules)
        {
            context.WeightCheckSchedules.Add(new WeightCheckSchedule
            {
                Id = Guid.NewGuid(), FarmId = farmId, AnimalTypeId = s.Type.Id,
                AgeCategoryId = null, RecurrenceDays = s.Days,
                IsActive = true, Notes = s.Notes, CreatedAt = now.AddMonths(-3),
            });
        }
        await context.SaveChangesAsync();

        // ── Feed: types, stock, records, diet plans, schedules, tasks ────────────────────
        var feedTypes = new[]
        {
            await EnsureFeedTypeAsync(context, farmId, now, "Lucerne Hay", FeedCategory.Forage, FeedUnit.Bale, 18m, "High-quality baled lucerne."),
            await EnsureFeedTypeAsync(context, farmId, now, "Maize Silage", FeedCategory.Forage, FeedUnit.Ton, 45m, "Home-grown maize silage."),
            await EnsureFeedTypeAsync(context, farmId, now, "Dairy Protein Concentrate", FeedCategory.Concentrate, FeedUnit.Kilogram, 0.92m, "High-protein concentrate for the milking herd."),
            await EnsureFeedTypeAsync(context, farmId, now, "Dairy Mineral Block", FeedCategory.Mineral, FeedUnit.Other, 22m, "Rigid mineral block, ad lib."),
            await EnsureFeedTypeAsync(context, farmId, now, "Grass Hay", FeedCategory.Forage, FeedUnit.Bale, 12m, "Mixed grass hay for dry stock."),
            await EnsureFeedTypeAsync(context, farmId, now, "Wheat Bran", FeedCategory.Concentrate, FeedUnit.Kilogram, 0.35m, "Energy supplement."),
            await EnsureFeedTypeAsync(context, farmId, now, "Poultry Layer Mash", FeedCategory.Concentrate, FeedUnit.Kilogram, 0.55m, "Complete layer ration."),
            await EnsureFeedTypeAsync(context, farmId, now, "Sheep Mineral Mix", FeedCategory.Mineral, FeedUnit.Kilogram, 1.20m, "Loose mineral mix for sheep and goats."),
        };

        for (var i = 0; i < 20; i++)
        {
            var feed = feedTypes[i % feedTypes.Length];
            var type = i % 5 == 0
                ? StockMovementType.Consumption
                : i % 7 == 0 ? StockMovementType.Adjustment : StockMovementType.Purchase;
            var quantity = type == StockMovementType.Consumption ? 4 + (i % 8) : 10 + (i % 15);
            await EnsureFeedStockMovementAsync(context, farmId, now, feed.Id, type, quantity,
                feed.CostPerUnit, type == StockMovementType.Purchase ? "Sunrise Feeds Co." : null,
                now.AddDays(-(5 + i * 3)));
        }

        for (var i = 0; i < 30; i++)
        {
            var feed = feedTypes[i % feedTypes.Length];
            var record = new FeedRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId, FeedTypeId = feed.Id,
                Quantity = 1 + (i % 6), UnitCost = feed.CostPerUnit,
                FedAt = now.AddDays(-(1 + (i % 14))),
                Notes = i % 4 == 0 ? "Morning group feed." : "Routine feeding record.",
            };
            if (i % 3 == 0)
                record.LocationId = locations.Values.ElementAt(i % locations.Count).Id;
            else
                record.AnimalId = animals[(i * 5) % animals.Count].Id;
            context.FeedRecords.Add(record);
        }

        var dietPlans = new[]
        {
            await EnsureDietPlanAsync(context, farmId, now, cattleType.Id, animalTypes["Cattle"].Breeds.First(b => b.Name == "Sahiwal").Id,
                "Milking Cow Ration", "Balanced ration for mid-lactation cows.",
                [(feedTypes[2].Id, 5.5m), (feedTypes[0].Id, 12m), (feedTypes[3].Id, 0.5m)]),
            await EnsureDietPlanAsync(context, farmId, now, cattleType.Id, null,
                "Dry Cow Maintenance", "Low-energy maintenance ration for dry cows.",
                [(feedTypes[4].Id, 10m), (feedTypes[5].Id, 2m), (feedTypes[3].Id, 0.2m)]),
            await EnsureDietPlanAsync(context, farmId, now, cattleType.Id, null,
                "Growing Calf Ration", "Growth ration for calves and yearlings.",
                [(feedTypes[2].Id, 2m), (feedTypes[0].Id, 3m), (feedTypes[3].Id, 0.1m)]),
            await EnsureDietPlanAsync(context, farmId, now, goatType.Id, animalTypes["Goat"].Breeds.First(b => b.Name == "Beetal").Id,
                "Goat Browse Mix", "Supplementary ration for browsing goats.",
                [(feedTypes[4].Id, 2m), (feedTypes[5].Id, 0.5m), (feedTypes[7].Id, 0.1m)]),
            await EnsureDietPlanAsync(context, farmId, now, poultryType.Id, animalTypes["Poultry"].Breeds.First().Id,
                "Poultry Layer Ration", "Complete layer ration for the flock.",
                [(feedTypes[6].Id, 0.12m), (feedTypes[5].Id, 0.02m), (feedTypes[3].Id, 0.01m)]),
        };

        var feedingSchedules = new[]
        {
            await EnsureFeedingScheduleAsync(context, farmId, now, dietPlans[0].Id, TimeSpan.FromHours(6), "Morning milking feed"),
            await EnsureFeedingScheduleAsync(context, farmId, now, dietPlans[0].Id, new TimeSpan(17, 30, 0), "Evening feed"),
            await EnsureFeedingScheduleAsync(context, farmId, now, dietPlans[1].Id, TimeSpan.FromHours(7), "Dry cow feed"),
            await EnsureFeedingScheduleAsync(context, farmId, now, dietPlans[2].Id, TimeSpan.FromHours(8), "Calf feed"),
            await EnsureFeedingScheduleAsync(context, farmId, now, dietPlans[3].Id, new TimeSpan(8, 30, 0), "Goat feed"),
            await EnsureFeedingScheduleAsync(context, farmId, now, dietPlans[4].Id, new TimeSpan(6, 30, 0), "Poultry feed"),
        };

        for (var i = 0; i < 20; i++)
        {
            var schedule = feedingSchedules[i % feedingSchedules.Length];
            var status = i % 7 == 0 ? FeedingTaskStatus.Skipped
                : i % 4 == 0 ? FeedingTaskStatus.Pending : FeedingTaskStatus.Completed;
            context.FeedingTasks.Add(new FeedingTask
            {
                Id = Guid.NewGuid(), FarmId = farmId,
                FeedingScheduleId = schedule.Id, DietPlanId = schedule.DietPlanId,
                TaskDate = now.Date.AddDays(-(i % 7)),
                TimeOfDay = schedule.TimeOfDay, Status = status,
                CompletedAt = status == FeedingTaskStatus.Completed
                    ? now.Date.AddDays(-(i % 7)).Add(schedule.TimeOfDay)
                    : null,
                TargetAnimalCount = 4 + (i % 8),
                Notes = status == FeedingTaskStatus.Skipped ? "Skipped due to weather." : null,
            });
        }
        await context.SaveChangesAsync();

        // ── Inventory: items, movements, suppliers, purchases, customers, sales ──────────
        var inventoryItems = new[]
        {
            await EnsureInventoryItemAsync(context, farmId, now, "Lucerne Hay Bales", "Forage", "Bale", 18m, 18, 10),
            await EnsureInventoryItemAsync(context, farmId, now, "Maize Silage Bags", "Forage", "Bag", 25m, 40, 20),
            await EnsureInventoryItemAsync(context, farmId, now, "FMD Vaccine Doses", "Health", "Dose", 12m, 8, 15),
            await EnsureInventoryItemAsync(context, farmId, now, "Antibiotic Vials", "Health", "Vial", 8m, 20, 10),
            await EnsureInventoryItemAsync(context, farmId, now, "Milking Gloves", "Dairy", "Pack", 6m, 25, 10),
            await EnsureInventoryItemAsync(context, farmId, now, "Teat Dip", "Dairy", "Liter", 9m, 14, 8),
            await EnsureInventoryItemAsync(context, farmId, now, "Fence Wire", "Maintenance", "Roll", 45m, 6, 4),
            await EnsureInventoryItemAsync(context, farmId, now, "Water Trough", "Maintenance", "Unit", 120m, 3, 2),
            await EnsureInventoryItemAsync(context, farmId, now, "Poultry Feed Bags", "Feed", "Bag", 22m, 30, 12),
            await EnsureInventoryItemAsync(context, farmId, now, "Egg Trays", "Poultry", "Tray", 1.5m, 80, 30),
            await EnsureInventoryItemAsync(context, farmId, now, "Ear Tags", "Identification", "Pack", 15m, 10, 5),
            await EnsureInventoryItemAsync(context, farmId, now, "Rope Halter", "Equipment", "Unit", 8m, 12, 6),
        };

        for (var i = 0; i < 24; i++)
        {
            var item = inventoryItems[i % inventoryItems.Length];
            var type = i % 5 == 0 ? InventoryMovementType.Consumption : InventoryMovementType.Purchase;
            await AddStockMovementAtAsync(context, farmId, item.Id, type, 1 + (i % 12),
                now.AddDays(-(3 + i * 2)), type == InventoryMovementType.Purchase ? "Stock replenishment" : "Used on the farm");
        }

        var suppliers = new[]
        {
            await EnsureSupplierAsync(context, farmId, now, "Sunrise Feeds Co.", "feed@sunrisefeeds.example", "Farm feeds, forage and concentrates"),
            await EnsureSupplierAsync(context, farmId, now, "AgriSupply", "orders@agrisupply.example", "Fencing, tools and dairy supplies"),
            await EnsureSupplierAsync(context, farmId, now, "DairyCo Feeds", "sales@dairyco.example", "Dairy concentrates and minerals"),
            await EnsureSupplierAsync(context, farmId, now, "Veterinary Wholesale", "vet@vetwholesale.example", "Medicines and vaccines"),
        };
        var customers = new[]
        {
            await EnsureCustomerAsync(context, farmId, now, "Green Valley Dairy", "sales@greenvalley.example"),
            await EnsureCustomerAsync(context, farmId, now, "Local Butcher", "orders@localbutcher.example"),
            await EnsureCustomerAsync(context, farmId, now, "City Poultry Market", "market@citypoultry.example"),
        };

        for (var i = 0; i < 6; i++)
        {
            await EnsureSupplierPurchaseAsync(context, farmId, now, suppliers[i % suppliers.Length].Id,
                inventoryItems[i % inventoryItems.Length].Id, 5 + i * 2, 180m + i * 65m,
                "Routine purchase", now.AddDays(-(10 + i * 9)));
            await EnsureCustomerSaleAsync(context, farmId, now, customers[i % customers.Length].Id,
                inventoryItems[(i + 4) % inventoryItems.Length].Id, 2 + i, 90m + i * 40m,
                "Farm-gate sale", now.AddDays(-(4 + i * 6)));
        }

        // ── Finance: expenses, income, payroll ───────────────────────────────────────────
        var expenseDescriptions = new[]
        {
            "Feed purchase — lucerne hay", "Veterinary treatment", "Fence repair",
            "Milking supplies", "Poultry feed", "Fuel and transport",
        };
        for (var i = 0; i < 30; i++)
        {
            var category = i % 4 == 0 ? labourCat : i % 3 == 0 ? feedCat : i % 3 == 1 ? healthCat : maintCat;
            var payment = i % 3 == 0 ? bank : i % 3 == 1 ? cash : mobile;
            context.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(), FarmId = farmId,
                ExpenseDate = now.AddDays(-(1 + (i * 3) % 120)),
                Amount = 25m + (i * 17 % 480),
                ExpenseCategoryId = category.Id, PaymentMethodId = payment.Id,
                AnimalId = i % 5 == 0 ? animals[i % animals.Count].Id : null,
                LocationId = i % 7 == 0 ? mainBarn.Id : null,
                Description = expenseDescriptions[i % expenseDescriptions.Length],
            });
        }

        for (var i = 0; i < 18; i++)
        {
            var category = i % 3 == 0 ? livestockCat : i % 3 == 1 ? poultryCat : milkCat;
            context.IncomeRecords.Add(new IncomeRecord
            {
                Id = Guid.NewGuid(), FarmId = farmId,
                IncomeDate = now.AddDays(-(2 + (i * 5) % 110)),
                Amount = 220m + (i * 41 % 1_800),
                IncomeCategoryId = category.Id,
                PaymentMethodId = (i % 2 == 0 ? bank : cash).Id,
                AnimalId = i % 4 == 0 ? animals[i % animals.Count].Id : null,
                Description = i % 3 == 0 ? "Livestock sale" : i % 3 == 1 ? "Poultry and egg sales" : "Milk delivery",
            });
        }

        for (var m = 1; m <= 3; m++)
        {
            foreach (var employee in employees)
            {
                context.SalaryPayments.Add(new SalaryPayment
                {
                    Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = employee.Id,
                    Amount = employee.SalaryRate,
                    PaymentDate = now.AddMonths(-m).AddDays(-2),
                    SalaryType = employee.SalaryType,
                    PaymentType = SalaryPaymentType.BankTransfer,
                    PeriodCovered = new DateTime(now.Year, now.Month, 1).AddMonths(-m),
                    Reference = $"SAL-{now.AddMonths(-m):yyyy-MM}",
                    Notes = "Monthly salary.",
                });
            }
        }

        // ── Tasks: general farm work and generated feeding work ──────────────────────────
        var taskTitles = new[]
        {
            "Repair south fence — paddock 2", "Vaccination round — FMD booster",
            "Prepare silage face for winter feeding", "Replace worn floodlight in dairy shed",
            "Deep-clean the milking parlour", "Order lucerne hay for next month",
            "Trim cattle hooves", "Check water troughs in North Pasture",
            "Weigh the weaner group", "Service the tractor",
            "Move dry cows to South Paddock", "Collect egg trays from the poultry house",
            "Inspect the maternity pens", "Top up mineral blocks",
            "Review staff rota for next week", "Repair the poultry house door",
            "Conduct a herd health walk", "Update the medicine register",
        };
        for (var i = 0; i < taskTitles.Length; i++)
        {
            var status = i % 6 == 0 ? FarmTaskStatus.Completed
                : i % 7 == 0 ? FarmTaskStatus.Cancelled
                : i % 5 == 0 ? FarmTaskStatus.InProgress : FarmTaskStatus.Pending;
            context.FarmTasks.Add(new FMS.Domain.Entities.FarmTask
            {
                Id = Guid.NewGuid(), FarmId = farmId,
                Title = taskTitles[i],
                Description = "Demo farm task — realistic work for the operations board.",
                Priority = (FarmTaskPriority)(1 + (i % 3)),
                Status = status,
                DueDate = now.AddDays((i % 9) - 4),
                StartedAt = status == FarmTaskStatus.InProgress ? now.AddDays(-1) : null,
                CompletedAt = status == FarmTaskStatus.Completed ? now.AddDays(-(i % 4 + 1)) : null,
                CompletionNotes = status == FarmTaskStatus.Completed ? "Completed by the farm team." : null,
                CancelReason = status == FarmTaskStatus.Cancelled ? "Deferred to next maintenance cycle." : null,
                AssignedEmployeeId = employees[i % employees.Length].Id,
                AnimalId = i % 4 == 0 ? animals[i % animals.Count].Id : null,
                LocationId = i % 3 == 0 ? locations.Values.ElementAt(i % locations.Count).Id : null,
            });
        }

        // ── Attendance and reviews ───────────────────────────────────────────────────────
        for (var day = 1; day <= 10; day++)
        {
            for (var e = 0; e < employees.Length; e++)
            {
                var status = ((day + e) % 11) switch
                {
                    0 => AttendanceStatus.Absent,
                    1 => AttendanceStatus.Late,
                    2 => AttendanceStatus.HalfDay,
                    3 => AttendanceStatus.Leave,
                    _ => AttendanceStatus.Present,
                };
                await EnsureAttendanceAsync(context, farmId, now, employees[e].Id, status, now.AddDays(-day));
            }
        }

        context.PerformanceReviews.AddRange(
            new PerformanceReview
            {
                Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = vet.Id,
                Rating = 4, ReviewDate = now.AddMonths(-2),
                PeriodStart = now.AddMonths(-8), PeriodEnd = now.AddMonths(-2),
                Strengths = "Excellent clinical work, responsive to ward calls.",
                AreasForImprovement = "Case notes could be more detailed.",
                ReviewedBy = manager.Id,
            },
            new PerformanceReview
            {
                Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = worker.Id,
                Rating = 3, ReviewDate = now.AddMonths(-1),
                PeriodStart = now.AddMonths(-7), PeriodEnd = now.AddMonths(-1),
                Strengths = "Reliable, good animal handling.",
                AreasForImprovement = "Time management on multi-task days.",
                ReviewedBy = manager.Id,
            },
            new PerformanceReview
            {
                Id = Guid.NewGuid(), FarmId = farmId, EmployeeId = manager.Id,
                Rating = 5, ReviewDate = now.AddMonths(-1),
                PeriodStart = now.AddMonths(-7), PeriodEnd = now.AddMonths(-1),
                Strengths = "Strong planning and team leadership.",
                AreasForImprovement = "Delegate more of the day-to-day scheduling.",
                ReviewedBy = vet.Id,
            });

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
