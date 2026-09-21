using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FMS.Application.Jobs;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Subphase 3.2 end-to-end coverage:
///
/// <list type="bullet">
/// <item>the scheduled feed-task job produces exactly the manual endpoint's
/// output for identical input — proven across two structurally identical farms;</item>
/// <item>the dashboard consumes the job's health snapshot when fresh and falls
/// back to a live calculation when stale, without reporting a degraded metric
/// either way;</item>
/// <item>the job-status endpoint is account-scoped (SystemOwner only, no farm
/// context required) and surfaces failures.</item>
/// </list>
///
/// Runs the REAL FarmContextMiddleware, so "the admin route needs no farm
/// context" is asserted against the production pipeline rather than a stub.
/// </summary>
public class JobsE2ETests : IClassFixture<JobsE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        /// <summary>The farm from ApiSeedData, driven through the manual endpoint.</summary>
        public Guid FarmAId { get; private set; }

        /// <summary>A structural clone of farm A, driven only by the scheduled job.</summary>
        public Guid FarmBId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            FarmAId = seed.FarmId;

            var sourceAnimals = await db.Animals
                .Where(a => a.FarmId == seed.FarmId)
                .AsNoTracking()
                .ToListAsync();

            await SeedFeedingSetupAsync(db, seed.FarmId, seed.AnimalTypeId, "Morning Mix");

            // ── Farm B: same account, same membership role, cloned structure ──
            var farmB = new Farm { Id = Guid.NewGuid(), AccountId = seed.AccountId, Name = "E2E Clone Farm" };
            db.Farms.Add(farmB);
            db.UserFarms.Add(new UserFarm
            {
                UserId = JwtTokenHelper.TestUserId,
                FarmId = farmB.Id,
                Role = "FarmManager"
            });

            var animalTypeB = new AnimalType { Id = Guid.NewGuid(), FarmId = farmB.Id, Name = "Cattle" };
            var statusB = new AnimalStatus
            {
                Id = Guid.NewGuid(), FarmId = farmB.Id, Name = "Active",
                Category = AnimalStatusCategory.Active, IsSystemDefined = true
            };
            db.AnimalTypes.Add(animalTypeB);
            db.AnimalStatuses.Add(statusB);

            // One clone per source animal, same breed/sex/age → identical dairy
            // population, so the diet plan's target-animal count matches.
            foreach (var source in sourceAnimals)
            {
                db.Animals.Add(new Animal
                {
                    Id = Guid.NewGuid(),
                    FarmId = farmB.Id,
                    TagNumber = $"CLONE-{source.TagNumber}",
                    Name = source.Name,
                    AnimalTypeId = animalTypeB.Id,
                    BreedId = source.BreedId,
                    SexOptionId = source.SexOptionId,
                    AnimalStatusId = statusB.Id,
                    DateOfBirth = source.DateOfBirth
                });
            }

            await db.SaveChangesAsync();
            await SeedFeedingSetupAsync(db, farmB.Id, animalTypeB.Id, "Morning Mix");

            FarmBId = farmB.Id;
        }

        /// <summary>Same feed type, same plan, same schedule for a farm.</summary>
        private static async Task SeedFeedingSetupAsync(FmsDbContext db, Guid farmId, Guid animalTypeId, string planName)
        {
            var hay = new FeedType
            {
                Id = Guid.NewGuid(), FarmId = farmId, Name = "Hay",
                Category = FeedCategory.Forage, Unit = FeedUnit.Kilogram, CostPerUnit = 5m
            };
            var plan = new DietPlan
            {
                Id = Guid.NewGuid(), FarmId = farmId, Name = planName,
                AnimalTypeId = animalTypeId, IsActive = true
            };
            var item = new DietPlanItem
            {
                Id = Guid.NewGuid(), DietPlanId = plan.Id, FeedTypeId = hay.Id, QuantityPerFeeding = 2.5m
            };
            var schedule = new FeedingSchedule
            {
                Id = Guid.NewGuid(), FarmId = farmId, DietPlanId = plan.Id,
                TimeOfDay = new TimeSpan(7, 0, 0), Label = "Morning", IsActive = true
            };

            db.FeedTypes.Add(hay);
            db.DietPlans.Add(plan);
            db.DietPlanItems.Add(item);
            db.FeedingSchedules.Add(schedule);
            await db.SaveChangesAsync();
        }
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public JobsE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public void Dispose() { }

    // ── DTOs ─────────────────────────────────────────────

    private class FeedingTaskItemResponse
    {
        public string FeedTypeName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal QuantityPerFeeding { get; set; }
    }

    private class FeedingTaskResponse
    {
        public Guid Id { get; set; }
        public Guid FeedingScheduleId { get; set; }
        public Guid DietPlanId { get; set; }
        public string DietPlanName { get; set; } = string.Empty;
        public DateTime TaskDate { get; set; }
        public string TimeOfDay { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public int TargetAnimalCount { get; set; }
        public List<FeedingTaskItemResponse> Items { get; set; } = new();
    }

    private class PagedResponse<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }

    private class SummaryResponse
    {
        public int DueVaccinationCount { get; set; }
        public int DueWeightCheckCount { get; set; }
        public DateTime? HealthSnapshotComputedAtUtc { get; set; }
        public List<string> DegradedMetrics { get; set; } = new();
    }

    private class JobStatusResponse
    {
        public bool Enabled { get; set; }
        public JobCountersResponse Counters { get; set; } = new();
        public List<RecurringJobResponse> RecurringJobs { get; set; } = new();
        public List<FailedJobResponse> RecentFailures { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    private class JobCountersResponse
    {
        public int Enqueued { get; set; }
        public int Failed { get; set; }
        public int Succeeded { get; set; }
    }

    private class RecurringJobResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Cron { get; set; } = string.Empty;
        public string? LastState { get; set; }
        public string? LastError { get; set; }
    }

    private class FailedJobResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? Job { get; set; }
        public string? ExceptionType { get; set; }
        public string? ExceptionMessage { get; set; }
    }

    // ── Helpers ──────────────────────────────────────────

    private HttpClient ClientFor(Guid? farmId, string accountRole = "SystemOwner")
    {
        _ = _factory.Host; // ensure seeded
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTokenHelper.GenerateTestToken(JwtTokenHelper.TestUserId, new[] { accountRole }));
        if (farmId.HasValue)
            client.DefaultRequestHeaders.Add("X-Farm-Id", farmId.Value.ToString());
        return client;
    }

    private async Task<int> RunFeedJobAsync(Guid farmId, DateTime date)
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<FeedingTaskGenerationJob>();
        return await job.ExecuteAsync(farmId, date);
    }

    private async Task SetSnapshotAsync(
        Guid farmId, int dueVaccinations, int overdueVaccinations, int dueWeightChecks, int overdueWeightChecks, DateTime computedAtUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        var snapshot = await db.FarmHealthStatusSnapshots.FirstOrDefaultAsync(s => s.FarmId == farmId);

        if (snapshot is null)
        {
            snapshot = new FarmHealthStatusSnapshot { Id = Guid.NewGuid(), FarmId = farmId };
            db.FarmHealthStatusSnapshots.Add(snapshot);
        }

        snapshot.DueVaccinationCount = dueVaccinations;
        snapshot.OverdueVaccinationCount = overdueVaccinations;
        snapshot.DueWeightCheckCount = dueWeightChecks;
        snapshot.OverdueWeightCheckCount = overdueWeightChecks;
        snapshot.ComputedAtUtc = computedAtUtc;

        await db.SaveChangesAsync();
    }

    /// <summary>Everything the endpoint returns except farm-specific identifiers.</summary>
    private static List<string> Normalize(IEnumerable<FeedingTaskResponse> tasks) => tasks
        .OrderBy(t => t.TimeOfDay, StringComparer.Ordinal)
        .Select(t => string.Join(" | ",
            t.TimeOfDay,
            t.StatusName,
            t.TargetAnimalCount,
            t.DietPlanName,
            string.Join("; ", t.Items
                .Select(i => $"{i.FeedTypeName} {i.QuantityPerFeeding} {i.UnitName}")
                .OrderBy(s => s, StringComparer.Ordinal))))
        .ToList();

    // ── 1. Scheduled job vs manual endpoint ──────────────

    [Fact]
    public async Task ScheduledJob_ProducesTheSameTasksAsTheManualEndpoint()
    {
        var date = DateTime.UtcNow.Date.AddDays(1);

        // Manual path: the existing endpoint, on farm A.
        var clientA = ClientFor(_factory.FarmAId);
        var manualResponse = await clientA.PostAsJsonAsync(
            $"/api/farm/{_factory.FarmAId}/feed/tasks/generate",
            new { date = date.ToString("yyyy-MM-ddTHH:mm:ssZ") });

        Assert.Equal(HttpStatusCode.OK, manualResponse.StatusCode);
        var manual = await manualResponse.Content.ReadFromJsonAsync<List<FeedingTaskResponse>>();
        Assert.NotNull(manual);
        Assert.NotEmpty(manual!);
        _output.WriteLine($"manual endpoint created {manual!.Count} task(s) for farm A");

        // Scheduled path: the exact method Hangfire invokes, on farm B with
        // identical seed data and no prior tasks.
        var clientB = ClientFor(_factory.FarmBId);
        var before = await clientB.GetFromJsonAsync<PagedResponse<FeedingTaskResponse>>(
            $"/api/farm/{_factory.FarmBId}/feed/tasks?date={date:yyyy-MM-dd}");
        Assert.Empty(before!.Items);

        var createdCount = await RunFeedJobAsync(_factory.FarmBId, date);

        var scheduled = await clientB.GetFromJsonAsync<PagedResponse<FeedingTaskResponse>>(
            $"/api/farm/{_factory.FarmBId}/feed/tasks?date={date:yyyy-MM-dd}");
        Assert.NotNull(scheduled);

        Assert.Equal(manual.Count, createdCount);
        Assert.Equal(Normalize(manual), Normalize(scheduled!.Items));

        // Guard against a vacuous comparison: both paths resolved the same
        // non-zero target population.
        Assert.Equal(8, manual[0].TargetAnimalCount);
        Assert.Equal(8, scheduled.Items[0].TargetAnimalCount);

        // Re-running the scheduled job adds nothing (the de-duplication lives in
        // the shared service, so this holds for the endpoint too).
        Assert.Equal(0, await RunFeedJobAsync(_factory.FarmBId, date));
    }

    // ── 2. Dashboard snapshot fast path ─────────────────

    [Fact]
    public async Task DashboardSummary_UsesAFreshSnapshotAndFallsBackToLiveCalculationWhenStale()
    {
        var client = ClientFor(_factory.FarmAId);

        // Fresh snapshot (what the health job writes): the dashboard must serve
        // it, which shows up as due + overdue, plus the computed-at marker.
        await SetSnapshotAsync(_factory.FarmAId, 5, 3, 2, 1, DateTime.UtcNow);

        var fresh = await client.GetFromJsonAsync<SummaryResponse>($"/api/farm/{_factory.FarmAId}/dashboard/summary");
        Assert.NotNull(fresh);
        Assert.Equal(8, fresh!.DueVaccinationCount);
        Assert.Equal(3, fresh.DueWeightCheckCount);
        Assert.NotNull(fresh.HealthSnapshotComputedAtUtc);
        Assert.Empty(fresh.DegradedMetrics);

        // Older than the staleness window: fall back to the live calculation.
        // The counts change (the seed has no vaccination/weight schedules, so the
        // live values are zero) and no metric is reported as degraded — the
        // fallback value is a real count, not a default.
        await SetSnapshotAsync(_factory.FarmAId, 5, 3, 2, 1, DateTime.UtcNow.AddHours(-6));

        var stale = await client.GetFromJsonAsync<SummaryResponse>($"/api/farm/{_factory.FarmAId}/dashboard/summary");
        Assert.NotNull(stale);
        Assert.Null(stale!.HealthSnapshotComputedAtUtc);
        Assert.Equal(0, stale.DueVaccinationCount);
        Assert.Equal(0, stale.DueWeightCheckCount);
        Assert.Empty(stale.DegradedMetrics);
    }

    // ── 3. Job status endpoint ──────────────────────────

    [Fact]
    public async Task AdminJobsEndpoint_RequiresAuthentication()
    {
        _ = _factory.Host;
        var client = _factory.CreateClient(); // no Authorization header

        var response = await client.GetAsync("/api/admin/jobs");

        _output.WriteLine($"anonymous -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("FarmManager")]
    [InlineData("Accountant")]
    [InlineData("Viewer")]
    public async Task AdminJobsEndpoint_RejectsNonSystemOwnerAccountRoles(string accountRole)
    {
        var client = ClientFor(null, accountRole);

        var response = await client.GetAsync("/api/admin/jobs");

        _output.WriteLine($"{accountRole} -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminJobsEndpoint_IsAccountScopedAndReportsFailures()
    {
        // No farm route and no X-Farm-Id: under the REAL FarmContextMiddleware the
        // farm-scoped gate must leave this route alone, and the caller's account
        // role (SystemOwner) decides access.
        var client = ClientFor(null);

        var response = await client.GetAsync("/api/admin/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<JobStatusResponse>();
        Assert.NotNull(status);
        Assert.True(status!.Enabled);

        var feedingJob = Assert.Single(status.RecurringJobs.Where(j => j.Id == JobNames.FeedingTaskGenerationFanOut));
        Assert.Equal("5 0 * * *", feedingJob.Cron);
        Assert.Equal("Succeeded", feedingJob.LastState);

        var healthJob = Assert.Single(status.RecurringJobs.Where(j => j.Id == JobNames.HealthStatusRecalculationFanOut));
        Assert.Equal("Failed", healthJob.LastState);
        Assert.Contains("connection refused", healthJob.LastError);

        // A failure must be visible rather than silent.
        var failure = Assert.Single(status.RecentFailures);
        Assert.Equal("HealthStatusRecalculationJob", failure.Job);
        Assert.Equal("InvalidOperationException", failure.ExceptionType);
        Assert.Contains("health-status-recalculation failed", failure.ExceptionMessage);
        Assert.Equal(3, status.Counters.Failed);
    }
}
