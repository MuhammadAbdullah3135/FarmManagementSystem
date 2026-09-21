using FMS.Application.Auth;
using FMS.Application.Breeding;
using FMS.Application.Common;
using FMS.Application.Dashboard;
using FMS.Application.Feed;
using FMS.Application.Finance;
using FMS.Application.Health;
using FMS.Application.Inventory;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Application.Tasks;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Breeding;
using FMS.Infrastructure.Dashboard;
using FMS.Infrastructure.Feed;
using FMS.Infrastructure.Finance;
using FMS.Infrastructure.Health;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Notifications;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FMS.Domain.Tests.Notifications;

/// <summary>
/// The notification dispatcher turns the dashboard's computed alerts into a
/// durable, deduplicated record per recipient. These tests run it against the
/// real <see cref="DashboardService"/> and the real service implementations, so
/// what they prove is the behaviour a scheduled run actually has:
/// <list type="bullet">
/// <item>a new condition notifies each eligible recipient exactly once, and later
/// runs do not repeat it;</item>
/// <item>a condition that merely changed refreshes its row instead of re-notifying;</item>
/// <item>a condition that cleared is resolved, and recurs as a fresh notification;</item>
/// <item>a disabled channel means no work at all, not work that is discarded;</item>
/// <item>an alert type whose metric failed is skipped, because "we could not tell"
/// must never be read as "it is fixed".</item>
/// </list>
///
/// <para>
/// Assertions read through <c>AsNoTracking</c> snapshots. Not a style choice: the
/// context here is the same one the dispatcher writes through, so a tracked entity
/// handed out before a run is the very instance the run mutates, and comparing it
/// with itself would pass vacuously.
/// </para>
/// </summary>
public class NotificationDispatcherTests
{
    // ── Harness ─────────────────────────────────────────────

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid? GetUserId() => Guid.NewGuid();
    }

    /// <summary>Captures digests, and can be made to fail for one address.</summary>
    private sealed class TestNotificationEmail : IEmailService
    {
        public List<string> SentTo { get; } = new();

        /// <summary>Address whose delivery throws, for the failure-visibility test.</summary>
        public string? FailFor { get; set; }

        public Task SendPasswordResetEmailAsync(string email, string resetLink) => Task.CompletedTask;

        public Task SendFarmInvitationEmailAsync(string email, string farmName, string inviteLink) =>
            Task.CompletedTask;

        public Task SendAlertNotificationsEmailAsync(
            string email,
            string farmName,
            IReadOnlyList<NotificationEmailItem> notifications)
        {
            if (FailFor is not null && string.Equals(FailFor, email, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("(test) provider rejected the message");
            }

            SentTo.Add(email);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The real weight-check service, switchable to throwing.
    ///
    /// Delegating rather than reimplementing keeps healthy runs honest — they run
    /// the same code the dashboard runs — while letting one alert type be made
    /// unavailable on demand.
    /// </summary>
    private sealed class StubWeightCheckScheduleService : IWeightCheckScheduleService
    {
        private readonly WeightCheckScheduleService _inner;

        public StubWeightCheckScheduleService(FmsDbContext context, ICurrentUserService currentUser) =>
            _inner = new WeightCheckScheduleService(context, currentUser);

        public bool Unavailable { get; set; }

        public Task<Result<PagedResult<WeightCheckScheduleDto>>> GetSchedulesAsync(Guid farmId, int page, int pageSize) =>
            _inner.GetSchedulesAsync(farmId, page, pageSize);

        public Task<Result<WeightCheckScheduleDto>> CreateScheduleAsync(Guid farmId, CreateWeightCheckScheduleRequest request) =>
            _inner.CreateScheduleAsync(farmId, request);

        public Task<Result<WeightCheckScheduleDto>> UpdateScheduleAsync(Guid farmId, Guid id, UpdateWeightCheckScheduleRequest request) =>
            _inner.UpdateScheduleAsync(farmId, id, request);

        public Task<Result> DeleteScheduleAsync(Guid farmId, Guid id) =>
            _inner.DeleteScheduleAsync(farmId, id);

        public Task<Result<List<WeightCheckStatusDto>>> GetWeightCheckStatusAsync(Guid farmId) =>
            Unavailable
                ? Task.FromException<Result<List<WeightCheckStatusDto>>>(UnavailableError())
                : _inner.GetWeightCheckStatusAsync(farmId);

        public Task<Result<List<WeightCheckStatusDto>>> GetOverdueWeightChecksAsync(Guid farmId) =>
            Unavailable
                ? Task.FromException<Result<List<WeightCheckStatusDto>>>(UnavailableError())
                : _inner.GetOverdueWeightChecksAsync(farmId);

        private static NotSupportedException UnavailableError() =>
            new("(test) the weight-check query is unavailable");
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Harness()
        {
            Context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

            var currentUser = new FixedCurrentUser();
            WeightChecks = new StubWeightCheckScheduleService(Context, currentUser);

            var services = new ServiceCollection();
            services.AddLogging();
            // The test's own context, so assertions read exactly what the
            // dispatcher wrote rather than a second view of the same store.
            services.AddSingleton(Context);
            services.AddSingleton<ICurrentUserService>(currentUser);
            services.AddSingleton<IEmailService>(Email);
            services.AddSingleton<IWeightCheckScheduleService>(WeightChecks);
            services.Configure<JobOptions>(_ => { });
            services.Configure<NotificationOptions>(_ => { });

            services.AddScoped<IVaccineService, VaccineService>();
            services.AddScoped<IMedicineService, MedicineService>();
            services.AddScoped<IFarmTaskService, FarmTaskService>();
            services.AddScoped<IBreedingService, BreedingService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IFeedService, FeedService>();
            services.AddScoped<IFinanceService, FinanceService>();
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

            _provider = services.BuildServiceProvider();
            Dispatcher = _provider.GetRequiredService<INotificationDispatcher>();
        }

        public FmsDbContext Context { get; }

        public INotificationDispatcher Dispatcher { get; }

        public TestNotificationEmail Email { get; } = new();

        public StubWeightCheckScheduleService WeightChecks { get; }

        public void Dispose()
        {
            _provider.Dispose();
            Context.Dispose();
        }
    }

    // ── Seed ────────────────────────────────────────────────

    private sealed record SeedMembers(
        Guid ManagerId,
        Guid VetId,
        Guid AccountantId,
        Guid ViewerId,
        string ManagerEmail,
        string VetEmail,
        string AccountantEmail);

    private sealed record SeedResult(Guid FarmId, Guid AnimalId, Guid VaccineTypeId, SeedMembers Members);

    /// <summary>
    /// One farm with a single overdue condition and one member per role that
    /// matters here.
    ///
    /// By default the only alert type present is OverdueVaccination (Critical):
    /// the vaccination schedule recurs every 30 days and the last dose was 40 days
    /// ago. Everything else is opt-in so each test states exactly which conditions
    /// it is exercising.
    ///
    /// <list type="bullet">
    /// <item><paramref name="includeWeightCheck"/> adds an overdue weight check.
    /// Note that the weight-check service materialises a follow-up <c>FarmTask</c>
    /// for overdue animals (pre-existing behaviour, covered by
    /// <c>WeightCheckScheduleServiceTests</c>), so this also produces an
    /// OverdueTask alert on the next computation — which is why the assertions
    /// that care about it filter by alert type.</item>
    /// <item><paramref name="includeLowStockMedicine"/> adds a medicine below its
    /// reorder level: a non-Critical alert type with no side effects.</item>
    /// </list>
    ///
    /// <paramref name="index"/> keeps two farms in one context from sharing user
    /// ids, so the isolation test can have both.
    /// </summary>
    private static async Task<SeedResult> SeedFarmAsync(
        FmsDbContext context,
        int index = 0,
        bool includeWeightCheck = false,
        bool includeLowStockMedicine = false)
    {
        Guid MemberId(int slot) => Guid.Parse($"cccccccc-0000-0000-{index:D4}-{slot:D12}");

        var managerId = MemberId(1);
        var vetId = MemberId(2);
        var accountantId = MemberId(3);
        var viewerId = MemberId(4);
        var managerEmail = $"manager{index}@notify.test";
        var vetEmail = $"vet{index}@notify.test";
        var accountantEmail = $"accountant{index}@notify.test";
        var viewerEmail = $"viewer{index}@notify.test";

        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = $"Notify Farm {index}" };
        var animalType = new AnimalType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Cattle" };
        var sex = new SexOption { Id = Guid.NewGuid(), FarmId = farm.Id, Value = "Female" };
        var status = new AnimalStatus
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Active", Category = AnimalStatusCategory.Active
        };
        var vaccineType = new VaccineType { Id = Guid.NewGuid(), FarmId = farm.Id, Name = "FMD" };

        var animal = new Animal
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, TagNumber = $"LATE-{index:D3}", Name = "Late Cow",
            AnimalTypeId = animalType.Id, SexOptionId = sex.Id, AnimalStatusId = status.Id,
            DateOfBirth = DateTime.UtcNow.AddYears(-2)
        };

        context.Farms.Add(farm);
        context.AnimalTypes.Add(animalType);
        context.SexOptions.Add(sex);
        context.AnimalStatuses.Add(status);
        context.VaccineTypes.Add(vaccineType);
        context.Animals.Add(animal);

        context.VaccinationSchedules.Add(new VaccinationSchedule
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, VaccineTypeId = vaccineType.Id,
            AnimalTypeId = animalType.Id, RecurrenceDays = 30, IsActive = true
        });
        context.VaccinationRecords.Add(new VaccinationRecord
        {
            Id = Guid.NewGuid(), FarmId = farm.Id, AnimalId = animal.Id,
            VaccineTypeId = vaccineType.Id, DateGiven = DateTime.UtcNow.Date.AddDays(-40)
        });

        if (includeWeightCheck)
        {
            context.WeightCheckSchedules.Add(new WeightCheckSchedule
            {
                Id = Guid.NewGuid(), FarmId = farm.Id,
                AnimalTypeId = animalType.Id, RecurrenceDays = 7, IsActive = true
            });
            context.WeightRecords.Add(new WeightRecord
            {
                Id = Guid.NewGuid(), FarmId = farm.Id, AnimalId = animal.Id,
                WeightKg = 400, RecordedAt = DateTime.UtcNow.Date.AddDays(-30)
            });
        }

        if (includeLowStockMedicine)
        {
            context.Medicines.Add(new Medicine
            {
                Id = Guid.NewGuid(), FarmId = farm.Id, Name = "Oxytetracycline", Unit = "ml",
                LowStockThreshold = 50, ExpiringSoonDays = 30
            });
        }

        context.Users.AddRange(
            NewUser(managerId, managerEmail, "Marta", "Manager"),
            NewUser(vetId, vetEmail, "Vic", "Vet"),
            NewUser(accountantId, accountantEmail, "Ada", "Accountant"),
            NewUser(viewerId, viewerEmail, "Val", "Viewer"));

        context.UserFarms.AddRange(
            NewMembership(farm.Id, managerId, "FarmManager"),
            NewMembership(farm.Id, vetId, "Veterinarian"),
            NewMembership(farm.Id, accountantId, "Accountant"),
            NewMembership(farm.Id, viewerId, "Viewer"));

        await context.SaveChangesAsync();

        return new SeedResult(
            farm.Id,
            animal.Id,
            vaccineType.Id,
            new SeedMembers(managerId, vetId, accountantId, viewerId, managerEmail, vetEmail, accountantEmail));
    }

    private static User NewUser(Guid id, string email, string first, string last) => new()
    {
        Id = id,
        AccountId = Guid.NewGuid(),
        Email = email,
        PasswordHash = "not-used-tests-invoke-the-dispatcher-directly",
        FirstName = first,
        LastName = last,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    private static UserFarm NewMembership(Guid farmId, Guid userId, string role) => new()
    {
        Id = Guid.NewGuid(),
        FarmId = farmId,
        UserId = userId,
        Role = role,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>The source key the dashboard builds for the seeded overdue vaccination.</summary>
    private static string VaccinationKey(SeedResult seed) =>
        NotificationAlertTypes.SourceKeys.ForOverdueVaccination(seed.AnimalId, seed.VaccineTypeId);

    private static string WeightCheckKey(SeedResult seed) =>
        NotificationAlertTypes.SourceKeys.ForOverdueWeightCheck(seed.AnimalId);

    /// <summary>Open notifications for one recipient, as an independent snapshot.</summary>
    private static Task<List<Notification>> OpenForAsync(FmsDbContext context, Guid farmId, Guid userId) =>
        context.Notifications
            .AsNoTracking()
            .Where(n => n.FarmId == farmId && n.UserId == userId && n.ResolvedAtUtc == null)
            .ToListAsync();

    /// <summary>The recipient's rows for one condition, newest first, as a snapshot.</summary>
    private static Task<List<Notification>> ForKeyAsync(FmsDbContext context, Guid userId, string sourceKey) =>
        context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.SourceKey == sourceKey)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

    private static async Task<Notification> SingleForKeyAsync(FmsDbContext context, Guid userId, string sourceKey)
    {
        var rows = await ForKeyAsync(context, userId, sourceKey);
        return Assert.Single(rows);
    }

    private static Task<Notification> TrackedForKeyAsync(FmsDbContext context, Guid userId, string sourceKey) =>
        context.Notifications.SingleAsync(n => n.UserId == userId && n.SourceKey == sourceKey);

    // ── Creation and de-duplication ─────────────────────────

    [Fact]
    public async Task ExecuteAsync_NewConditions_CreateExactlyOneNotificationPerEligibleRecipient()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        var result = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // One alert (the overdue vaccination) × three eligible recipients: the
        // manager, the vet and the accountant. The Viewer is deliberately not in
        // the audience for this alert type.
        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Resolved);

        var manager = await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ManagerId);
        var single = Assert.Single(manager);
        Assert.Equal(NotificationAlertTypes.OverdueVaccination, single.AlertType);

        Assert.Single(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.VetId));
        Assert.Single(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.AccountantId));
        Assert.Empty(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ViewerId));

        // The notification carries what the dashboard showed, plus the identity of
        // the condition it came from.
        var vaccination = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));
        Assert.Equal(NotificationSeverity.Critical, vaccination.Severity);
        Assert.Equal("/dashboard/health/vaccinations", vaccination.Link);
        Assert.True(vaccination.IsOpen);
        Assert.Null(vaccination.ReadAtUtc);
        Assert.Null(vaccination.ResolvedAtUtc);
    }

    [Fact]
    public async Task ExecuteAsync_RepeatedRuns_DoNotDuplicateAnUnchangedCondition()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        var first = await harness.Dispatcher.ExecuteAsync(seed.FarmId);
        var second = await harness.Dispatcher.ExecuteAsync(seed.FarmId);
        var third = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        Assert.Equal(3, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, third.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, third.Updated);

        // Exactly one row per (recipient, condition) — not one per run.
        Assert.Equal(3, await harness.Context.Notifications.CountAsync(n => n.FarmId == seed.FarmId));
        Assert.Single(await ForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed)));
    }

    [Fact]
    public async Task ExecuteAsync_ChangedCondition_RefreshesTheSameRowWithoutNotifyingAgain()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        var before = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));

        // Move the same problem earlier: still overdue, different due date and
        // therefore different text. Identity (the source key) is unchanged.
        var record = await harness.Context.VaccinationRecords
            .SingleAsync(r => r.AnimalId == seed.AnimalId);
        record.DateGiven = record.DateGiven.AddDays(-20);
        await harness.Context.SaveChangesAsync();

        var result = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        var after = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));

        Assert.Equal(0, result.Created);
        Assert.Equal(3, result.Updated); // one per eligible recipient
        Assert.Equal(before.Id, after.Id);
        Assert.NotEqual(before.Message, after.Message);
        Assert.NotEqual(before.DueDate, after.DueDate);
        Assert.Equal(before.CreatedAt, after.CreatedAt);

        // Refreshing is not re-delivery: the row keeps its delivery state and its
        // read state.
        Assert.Equal(before.DeliveredAtUtc, after.DeliveredAtUtc);
        Assert.Equal(before.ReadAtUtc, after.ReadAtUtc);

        Assert.Single(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ManagerId));
    }

    [Fact]
    public async Task ExecuteAsync_ClearedCondition_IsResolvedAndRecurringConditionStartsFresh()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        var original = await TrackedForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));
        var originalId = original.Id;

        // The recipient saw it and cleared it.
        original.DismissedAtUtc = DateTime.UtcNow;
        original.ReadAtUtc = DateTime.UtcNow;
        await harness.Context.SaveChangesAsync();

        // Vaccinate today: the condition is gone.
        var newRecord = new VaccinationRecord
        {
            Id = Guid.NewGuid(), FarmId = seed.FarmId, AnimalId = seed.AnimalId,
            VaccineTypeId = seed.VaccineTypeId, DateGiven = DateTime.UtcNow.Date
        };
        harness.Context.VaccinationRecords.Add(newRecord);
        await harness.Context.SaveChangesAsync();

        var cleared = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        Assert.Equal(3, cleared.Resolved); // one per eligible recipient
        var resolved = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));
        Assert.NotNull(resolved.ResolvedAtUtc);
        Assert.Empty(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ManagerId));

        // The condition comes back (the record is undone): it must alert again —
        // as a new row, not by resurrecting the dismissed one.
        harness.Context.VaccinationRecords.Remove(newRecord);
        await harness.Context.SaveChangesAsync();

        var recurred = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        Assert.Equal(3, recurred.Created);
        var open = Assert.Single(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ManagerId));

        Assert.NotEqual(originalId, open.Id);
        Assert.Null(open.DismissedAtUtc);
        Assert.Null(open.ReadAtUtc);

        // The closed row survives as history.
        Assert.Equal(2, (await ForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed))).Count);
    }

    [Fact]
    public async Task ExecuteAsync_RecipientWithoutAnEligibleRole_IsNeverNotified()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // Viewer is the read-only role, absent from every restricted module, so it
        // receives nothing: alerting somebody who cannot act is noise.
        Assert.Empty(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ViewerId));
    }

    // ── Preferences / channels ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmailDisabledForAnAlertType_DeliversThatChannelToNobody()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        // The manager turns email off for overdue vaccinations. The condition is
        // Critical, which is emailable by default, so this is a real override.
        harness.Context.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            UserId = seed.Members.ManagerId,
            AlertType = NotificationAlertTypes.OverdueVaccination,
            InAppEnabled = true,
            EmailEnabled = false,
            CreatedAt = DateTime.UtcNow
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // The channel is skipped as work, not performed and discarded.
        Assert.DoesNotContain(seed.Members.ManagerEmail, harness.Email.SentTo);
        Assert.Contains(seed.Members.VetEmail, harness.Email.SentTo);
        Assert.Contains(seed.Members.AccountantEmail, harness.Email.SentTo);
        Assert.Equal(2, result.EmailsSent);

        // In-app is untouched, and the row records no delivery for the manager.
        var managerVaccination = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));
        Assert.Null(managerVaccination.DeliveredAtUtc);
        Assert.Equal(0, managerVaccination.DeliveryAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_NonCriticalAlert_IsInAppOnlyByDefault()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context, includeLowStockMedicine: true);

        await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // Email defaults to Critical-only, so the medicine alert is recorded
        // in-app and never mailed — while the Critical vaccination is.
        var manager = await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ManagerId);
        var medicine = Assert.Single(manager, n => n.AlertType == NotificationAlertTypes.Medicine);
        var vaccination = Assert.Single(manager, n => n.AlertType == NotificationAlertTypes.OverdueVaccination);

        Assert.Equal(0, medicine.DeliveryAttempts);
        Assert.Null(medicine.DeliveredAtUtc);
        Assert.Equal(1, vaccination.DeliveryAttempts);
        Assert.NotNull(vaccination.DeliveredAtUtc);
    }

    [Fact]
    public async Task ExecuteAsync_InAppDisabledForAnAlertType_CreatesNoNotification()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        harness.Context.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(),
            FarmId = seed.FarmId,
            UserId = seed.Members.AccountantId,
            AlertType = NotificationAlertTypes.OverdueVaccination,
            InAppEnabled = false,
            EmailEnabled = true,
            CreatedAt = DateTime.UtcNow
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // The recipient opted out entirely: no row, and therefore nothing to
        // deliver, even though email was enabled.
        Assert.Empty(await OpenForAsync(harness.Context, seed.FarmId, seed.Members.AccountantId));
        Assert.DoesNotContain(seed.Members.AccountantEmail, harness.Email.SentTo);
        Assert.Equal(2, result.Created);
    }

    [Fact]
    public async Task ExecuteAsync_NoStoredPreference_UsesTheDefaults()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        // Nobody has opened the preferences screen.
        Assert.Empty(harness.Context.NotificationPreferences);

        await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // Defaults are in-app on, and email on for Critical only — the vaccination
        // alert is Critical, so every eligible recipient is mailed.
        Assert.Contains(seed.Members.ManagerEmail, harness.Email.SentTo);
        Assert.Equal(3, await harness.Context.Notifications.CountAsync(n => n.FarmId == seed.FarmId));
    }

    // ── Availability of the underlying metric ───────────────

    [Fact]
    public async Task ExecuteAsync_MetricFailure_SkipsThatTypeAndLeavesItsNotificationsAlone()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context, includeWeightCheck: true);

        // A healthy run first, so there is a live weight-check notification to
        // protect.
        await harness.Dispatcher.ExecuteAsync(seed.FarmId);
        var weightCheckBefore = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, WeightCheckKey(seed));

        // Now the weight-check query starts failing.
        harness.WeightChecks.Unavailable = true;

        var result = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // The failure is reported as a skipped type, not as a cleared condition.
        Assert.Contains(NotificationAlertTypes.OverdueWeightCheck, result.SkippedAlertTypes);
        Assert.DoesNotContain(NotificationAlertTypes.OverdueVaccination, result.SkippedAlertTypes);
        Assert.Equal(0, result.Resolved);
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Updated);

        var weightCheckAfter = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, WeightCheckKey(seed));
        Assert.Null(weightCheckAfter.ResolvedAtUtc);
        Assert.Equal(weightCheckBefore.Id, weightCheckAfter.Id);

        // The available types are still reconciled normally.
        var vaccination = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));
        Assert.Null(vaccination.ResolvedAtUtc);
        Assert.Equal(3, (await OpenForAsync(harness.Context, seed.FarmId, seed.Members.ManagerId)).Count);
    }

    // ── Delivery failure visibility ─────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmailFailure_IsRecordedOnTheNotificationNotThrown()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        harness.Email.FailFor = seed.Members.ManagerEmail;

        var result = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        // Not thrown: a dead address is one recipient's problem, and the in-app
        // notification (the primary channel) is already persisted.
        Assert.Equal(1, result.EmailFailures);
        Assert.Equal(2, result.EmailsSent);

        var managerVaccination = await SingleForKeyAsync(harness.Context, seed.Members.ManagerId, VaccinationKey(seed));
        Assert.Equal(1, managerVaccination.DeliveryAttempts);
        Assert.Null(managerVaccination.DeliveredAtUtc);
        Assert.Contains("provider rejected", managerVaccination.LastDeliveryError!);
    }

    // ── Multi-tenancy ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoMembers_DoesNotThrow()
    {
        using var harness = new Harness();
        var seed = await SeedFarmAsync(harness.Context);

        harness.Context.UserFarms.RemoveRange(harness.Context.UserFarms);
        await harness.Context.SaveChangesAsync();

        var result = await harness.Dispatcher.ExecuteAsync(seed.FarmId);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Resolved);
        Assert.Empty(harness.Context.Notifications);
    }

    [Fact]
    public async Task ExecuteAsync_OnlyEverWritesToTheGivenFarm()
    {
        using var harness = new Harness();
        var farmA = await SeedFarmAsync(harness.Context, index: 0);
        var farmB = await SeedFarmAsync(harness.Context, index: 1);

        await harness.Dispatcher.ExecuteAsync(farmA.FarmId);

        Assert.NotEmpty(harness.Context.Notifications.Where(n => n.FarmId == farmA.FarmId));
        Assert.Empty(harness.Context.Notifications.Where(n => n.FarmId == farmB.FarmId));
    }
}

/// <summary>
/// The job wrapper only adds job-level logging and the fail-loudly contract, so
/// that is all it is tested for.
/// </summary>
public class NotificationDispatchJobTests
{
    private sealed class StubDispatcher : INotificationDispatcher
    {
        public NotificationDispatchResult Result { get; set; } =
            new(4, 1, 2, 3, 0, new[] { NotificationAlertTypes.Medicine });

        public Exception? Throw { get; set; }

        public Task<NotificationDispatchResult> ExecuteAsync(Guid farmId, CancellationToken cancellationToken = default) =>
            Throw is null
                ? Task.FromResult(Result)
                : Task.FromException<NotificationDispatchResult>(Throw);
    }

    [Fact]
    public async Task ExecuteAsync_LogsTheOutcomeWithTheJobNameAndFarm()
    {
        var stub = new StubDispatcher();
        var (logger, logs) = TestLoggers.Create<NotificationDispatchJob>();
        var job = new NotificationDispatchJob(stub, logger);
        var farmId = Guid.NewGuid();

        var result = await job.ExecuteAsync(farmId);

        Assert.Equal(stub.Result, result);

        var info = Assert.Single(logs.ForLevel(LogLevel.Information));
        Assert.Contains(JobNames.NotificationDispatch, info.Message);
        Assert.Contains(farmId.ToString(), info.Message);
        Assert.Contains("4 created", info.Message);
        Assert.Contains("2 resolved", info.Message);
        Assert.Contains("3 digest(s) sent", info.Message);
        Assert.Contains("1 alert type(s) skipped", info.Message);
    }

    [Fact]
    public async Task ExecuteAsync_DispatchFailed_IsLoggedAndRethrown()
    {
        var stub = new StubDispatcher { Throw = new InvalidOperationException("alert computation exploded") };
        var (logger, logs) = TestLoggers.Create<NotificationDispatchJob>();
        var job = new NotificationDispatchJob(stub, logger);
        var farmId = Guid.NewGuid();

        // Rethrown so the scheduler records the failure and applies the retry
        // policy — a swallowed failure would leave the farm silently un-notified.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(farmId));

        Assert.Equal("alert computation exploded", thrown.Message);

        var error = Assert.Single(logs.ForLevel(LogLevel.Error));
        Assert.Contains(JobNames.NotificationDispatch, error.Message);
        Assert.Contains(farmId.ToString(), error.Message);
        Assert.Equal(thrown, error.Exception);
    }
}
