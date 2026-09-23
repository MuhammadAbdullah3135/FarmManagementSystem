using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMS.Application.Sync;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The acceptance criteria for phase 5.3, run against the real request pipeline
/// (<c>UsesRealFarmContextMiddleware</c>) with the real workflows.
///
/// <para>
/// What has to be true: a batch sent twice creates one row and answers with the original
/// result both times; a mixed batch reports every item individually; a validation failure
/// says exactly what the single-record endpoint says for the same input; a device timestamp
/// is recorded as the device sent it; and the new farm-scoped route inherits the same
/// farm-context enforcement as every other one (non-member refused, header/route mismatch
/// refused).
/// </para>
/// </summary>
public class MutationSyncE2ETests : IClassFixture<MutationSyncE2ETests.Factory>, IDisposable
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public class Factory : TestWebApplicationFactory
    {
        public Factory()
        {
            // Seeding is what assigns every property below, and the host is built lazily. Touching it
            // here — in the fixture, before any test can read a property — is what keeps a test that
            // does ClientFor(MemberFarmId) from sending Guid.Empty as X-Farm-Id because the argument
            // was evaluated a moment before the first host access ran the seed.
            _ = Host;
        }

        /// <summary>The gate this exercises: membership and route/header enforcement are real.</summary>
        protected override bool UsesRealFarmContextMiddleware => true;

        /// <summary>Farm the seeded test user belongs to.</summary>
        public Guid MemberFarmId { get; private set; }

        /// <summary>Second farm the user also belongs to (for the header/route mismatch case).</summary>
        public Guid OtherMemberFarmId { get; private set; }

        /// <summary>Farm the user has no membership in.</summary>
        public Guid ForeignFarmId { get; private set; }

        public Guid ForeignAnimalId { get; private set; }

        public Guid AnimalId { get; private set; }

        public Guid PendingTaskId { get; private set; }

        public Guid CompletedTaskId { get; private set; }

        public Guid CancelledTaskId { get; private set; }

        /// <summary>One employee per attendance test, so a shared fixture cannot leak a day's record between them.</summary>
        public Guid AttendanceEmployeeId { get; private set; }

        public Guid CheckInConflictEmployeeId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            MemberFarmId = seed.FarmId;

            AnimalId = (await db.Animals.FirstAsync(a => a.FarmId == seed.FarmId)).Id;

            db.Employees.AddRange(
                NewEmployee(seed.FarmId, AttendanceEmployeeId = Guid.NewGuid(), "Grace", "Otieno"),
                NewEmployee(seed.FarmId, CheckInConflictEmployeeId = Guid.NewGuid(), "Peter", "Mwangi"));

            // The seed's own task is Pending, so it can be completed.
            PendingTaskId = (await db.FarmTasks.FirstAsync(t => t.FarmId == seed.FarmId)).Id;

            var completed = new FarmTask
            {
                Id = Guid.NewGuid(), FarmId = seed.FarmId, Title = "Already completed",
                Status = FarmTaskStatus.Completed, CompletedAt = DateTime.UtcNow.AddDays(-1),
                DueDate = DateTime.UtcNow.AddDays(-2)
            };
            var cancelled = new FarmTask
            {
                Id = Guid.NewGuid(), FarmId = seed.FarmId, Title = "Cancelled task",
                Status = FarmTaskStatus.Cancelled, CancelReason = "Not needed",
                DueDate = DateTime.UtcNow.AddDays(1)
            };
            db.FarmTasks.AddRange(completed, cancelled);
            CompletedTaskId = completed.Id;
            CancelledTaskId = cancelled.Id;

            // Farm B: same account, user IS a member — the route/header mismatch case.
            var farmB = new Farm { Id = Guid.NewGuid(), AccountId = seed.AccountId, Name = "Second Farm" };
            db.Farms.Add(farmB);
            db.UserFarms.Add(new UserFarm
            {
                UserId = JwtTokenHelper.TestUserId, FarmId = farmB.Id, Role = "FarmManager"
            });
            OtherMemberFarmId = farmB.Id;

            // Farm C: same account, user is NOT a member — its animal must stay unreachable.
            var farmC = new Farm { Id = Guid.NewGuid(), AccountId = seed.AccountId, Name = "Foreign Farm" };
            var typeC = new AnimalType { Id = Guid.NewGuid(), FarmId = farmC.Id, Name = "Cattle" };
            var statusC = new AnimalStatus
            {
                Id = Guid.NewGuid(), FarmId = farmC.Id, Name = "Active",
                Category = AnimalStatusCategory.Active, IsSystemDefined = true
            };
            var animalC = new Animal
            {
                Id = Guid.NewGuid(), FarmId = farmC.Id, TagNumber = "FARM-C-001", Name = "Farm C Cow",
                AnimalType = typeC, AnimalStatus = statusC, DateOfBirth = DateTime.UtcNow.AddYears(-2)
            };
            db.Farms.Add(farmC);
            db.AnimalTypes.Add(typeC);
            db.AnimalStatuses.Add(statusC);
            db.Animals.Add(animalC);
            ForeignFarmId = farmC.Id;
            ForeignAnimalId = animalC.Id;

            await db.SaveChangesAsync();
        }

        private static Employee NewEmployee(Guid farmId, Guid id, string first, string last) => new()
        {
            Id = id,
            FarmId = farmId,
            FirstName = first,
            LastName = last,
            SalaryType = SalaryType.Monthly,
            SalaryRate = 1000m,
            HireDate = DateTime.UtcNow.AddYears(-1),
            IsActive = true
        };
    }

    private readonly Factory _factory;
    private readonly ITestOutputHelper _output;

    public MutationSyncE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public void Dispose() { }

    // ── helpers ────────────────────────────────────────────

    private static string SyncRoute(Guid farmId) => $"/api/farm/{farmId}/sync/mutations";

    private static string WeightsRoute(Guid farmId, Guid animalId) =>
        $"/api/farm/{farmId}/animals/{animalId}/weights";

    private static string CompleteRoute(Guid farmId, Guid taskId) =>
        $"/api/farm/{farmId}/tasks/{taskId}/complete";

    private static string CheckInRoute(Guid farmId, Guid employeeId) =>
        $"/api/farm/{farmId}/attendance/{employeeId}/check-in";

    /// <summary>An authenticated client, optionally pinning the session farm with X-Farm-Id.</summary>
    private HttpClient ClientFor(Guid? headerFarm)
    {
        _ = _factory.Host;

        // A farm id that is not the seeded one is a bug in the test, not a farm-context failure:
        // say so here rather than letting a mismatch come back as a puzzling 403.
        Assert.NotEqual(Guid.Empty, headerFarm);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTokenHelper.GenerateTestToken(JwtTokenHelper.TestUserId, new[] { "FarmManager" }));

        if (headerFarm.HasValue)
            client.DefaultRequestHeaders.Add("X-Farm-Id", headerFarm.Value.ToString());

        return client;
    }

    private static JsonElement Payload(object value) =>
        JsonSerializer.SerializeToElement(value, WebOptions);

    private static object WeightItem(Guid mutationId, Guid animalId, decimal weightKg, DateTime? recordedAt = null) =>
        new
        {
            operation = SyncOperations.WeightRecord,
            clientMutationId = mutationId,
            payload = Payload(new { animalId, weightKg, recordedAt })
        };

    private static object CheckInItem(Guid mutationId, Guid employeeId, DateTime? occurredAt = null) =>
        new
        {
            operation = SyncOperations.AttendanceCheckIn,
            clientMutationId = mutationId,
            payload = Payload(new { employeeId, occurredAt })
        };

    private static object CheckOutItem(Guid mutationId, Guid employeeId, DateTime? occurredAt = null) =>
        new
        {
            operation = SyncOperations.AttendanceCheckOut,
            clientMutationId = mutationId,
            payload = Payload(new { employeeId, occurredAt })
        };

    private static object CompleteItem(
        Guid mutationId, Guid taskId, DateTime? occurredAt = null, string? completionNotes = null) =>
        new
        {
            operation = SyncOperations.TaskComplete,
            clientMutationId = mutationId,
            payload = Payload(new { taskId, occurredAt, completionNotes })
        };

    private Task<HttpResponseMessage> SendAsync(HttpClient client, Guid farmId, params object[] items) =>
        client.PostAsJsonAsync(SyncRoute(farmId), new { items });

    private static async Task<SyncMutationResultDto> ReadResultAsync(HttpResponseMessage response)
    {
        var result = await response.Content.ReadFromJsonAsync<SyncMutationResultDto>(ReadOptions);
        Assert.NotNull(result);
        return result!;
    }

    /// <summary>
    /// Reads a controller's error answer, which is the message itself (the controllers answer
    /// <c>BadRequest(error.Message)</c>, written as text/plain by the built-in string formatter).
    /// A JSON-quoted body is unwrapped too, so the comparison is about the message and not about
    /// which formatter won.
    /// </summary>
    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (body.StartsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(body, ReadOptions) ?? body;
            }
            catch (JsonException)
            {
                // Not JSON after all; the raw body is the message.
            }
        }

        return body.Trim();
    }

    private async Task<T> QueryAsync<T>(Func<FmsDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await query(db);
    }

    private Task<int> WeightCountAsync(Guid mutationId) =>
        QueryAsync(db => db.WeightRecords.CountAsync(w => w.ClientMutationId == mutationId));

    private Task<int> LedgerCountAsync(Guid mutationId) =>
        QueryAsync(db => db.ProcessedMutations.CountAsync(m => m.ClientMutationId == mutationId));    // ── the headline criterion: a retry applies once ───────

    [Fact]
    public async Task SameBatchSentTwice_CreatesOneRowAndReturnsTheOriginalResult()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();
        var recordedAt = DateTime.UtcNow.AddHours(-6);
        var body = new { items = new[] { WeightItem(mutationId, _factory.AnimalId, 512.25m, recordedAt) } };

        var first = await client.PostAsJsonAsync(SyncRoute(_factory.MemberFarmId), body);
        var firstBody = await first.Content.ReadAsStringAsync();
        var second = await client.PostAsJsonAsync(SyncRoute(_factory.MemberFarmId), body);
        var secondBody = await second.Content.ReadAsStringAsync();

        _output.WriteLine($"first:  {firstBody}");
        _output.WriteLine($"second: {secondBody}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // The whole answer, byte for byte — the second send is the stored original, not a
        // recomputed one (a fresh attempt would return a different created-row id).
        Assert.Equal(firstBody, secondBody);

        Assert.Equal(1, await WeightCountAsync(mutationId));
        Assert.Equal(1, await LedgerCountAsync(mutationId));

        var stored = await QueryAsync(db => db.WeightRecords
            .AsNoTracking()
            .SingleAsync(w => w.ClientMutationId == mutationId));

        Assert.Equal(512.25m, stored.WeightKg);
        Assert.Equal(_factory.AnimalId, stored.AnimalId);
        Assert.Equal(recordedAt, stored.RecordedAt); // the device's clock, not the sync time
    }

    [Fact]
    public async Task ReplayedMutation_ReturnsTheStoredResult_EvenWhenThePayloadChanges()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();

        var first = await SendAsync(client, _factory.MemberFarmId,
            WeightItem(mutationId, _factory.AnimalId, 480m));
        var firstBody = await first.Content.ReadAsStringAsync();

        // The same mutation id with a different weight: the mutation has already been applied,
        // so the answer must describe what happened, not what this payload asks for.
        var replay = await SendAsync(client, _factory.MemberFarmId,
            WeightItem(mutationId, _factory.AnimalId, 999m));
        var replayBody = await replay.Content.ReadAsStringAsync();

        Assert.Equal(firstBody, replayBody);
        Assert.Equal(1, await WeightCountAsync(mutationId));
        Assert.Equal(480m, await QueryAsync(db => db.WeightRecords
            .Where(w => w.ClientMutationId == mutationId).Select(w => w.WeightKg).SingleAsync()));
    }

    // ── a mixed batch reports every item ───────────────────

    [Fact]
    public async Task MixedBatch_ReportsEachItemIndividually_AndAppliesTheValidOne()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var goodId = Guid.NewGuid();
        var unknownAnimalId = Guid.NewGuid();
        var badWeightId = Guid.NewGuid();

        var response = await SendAsync(client, _factory.MemberFarmId,
            WeightItem(goodId, _factory.AnimalId, 505m),
            WeightItem(unknownAnimalId, unknownAnimalId, 500m),
            WeightItem(badWeightId, _factory.AnimalId, 0m));

        var result = await ReadResultAsync(response);

        _output.WriteLine(JsonSerializer.Serialize(result, ReadOptions));

        Assert.Equal(3, result.RequestedCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(0, result.SupersededCount);
        Assert.Equal(2, result.RejectedCount);

        Assert.Equal(SyncMutationOutcome.Accepted, result.Items[0].Outcome);
        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[1].Outcome);
        Assert.Equal("Animal not found", result.Items[1].Message);
        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[2].Outcome);
        Assert.Equal("Weight must be greater than zero", result.Items[2].Message);

        // The failure list keeps the import shape (position + message).
        Assert.Equal(2, result.Failures.Count);
        Assert.Equal(1, result.Failures[0].Index);
        Assert.Equal(2, result.Failures[1].Index);

        // The valid item landed; the refused ones left nothing behind — and, being refusals
        // that wrote nothing, they were not recorded as processed either.
        Assert.Equal(1, await WeightCountAsync(goodId));
        Assert.Equal(0, await WeightCountAsync(badWeightId));
        Assert.Equal(0, await LedgerCountAsync(badWeightId));
    }

    /// <summary>
    /// One batch carrying every queued workflow: the shape a device actually sends after a day
    /// offline with weights, attendance and tasks all waiting.
    ///
    /// <para>
    /// The per-workflow tests prove each payload and each conflict rule; what this one adds is
    /// that the three coexist in a single request — the server routes by operation, each item
    /// goes through its own service method, and one reconnect produces exactly three effects.
    /// Replaying the identical batch then has to answer identically and add nothing, because
    /// that is the retry a device makes after it is killed mid-flush.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MixedOperationsBatch_AppliesEveryWorkflowOnce_AndReplaysCleanly()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var weightId = Guid.NewGuid();
        var checkInId = Guid.NewGuid();
        var completionId = Guid.NewGuid();
        var deviceTime = DateTime.UtcNow.AddHours(-3);

        // This test owns the employee and the task it acts on. The fixture is shared across the
        // class (`IClassFixture`), so a seeded task another test completes — or a seeded
        // employee another test checks in — would make this one pass alone and fail in the
        // suite. The weight needs nothing of its own: it is identified by its mutation id.
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await QueryAsync(async db =>
        {
            db.Employees.Add(new Employee
            {
                Id = employeeId,
                FarmId = _factory.MemberFarmId,
                FirstName = "Hana",
                LastName = "Yusuf",
                SalaryType = SalaryType.Monthly,
                SalaryRate = 1000m,
                HireDate = DateTime.UtcNow.AddYears(-1),
                IsActive = true
            });
            db.FarmTasks.Add(new FarmTask
            {
                Id = taskId,
                FarmId = _factory.MemberFarmId,
                Title = "Mixed batch task",
                Status = FarmTaskStatus.Pending,
                DueDate = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
            return true;
        });

        var items = new object[]
        {
            WeightItem(weightId, _factory.AnimalId, 505m, deviceTime),
            CheckInItem(checkInId, employeeId, deviceTime),
            CompleteItem(completionId, taskId, deviceTime, "Fence repaired")
        };

        var first = await SendAsync(client, _factory.MemberFarmId, items);
        var firstBody = await first.Content.ReadAsStringAsync();
        var result = await ReadResultAsync(first);

        _output.WriteLine(firstBody);

        Assert.Equal(3, result.RequestedCount);
        Assert.Equal(3, result.SuccessCount);
        Assert.Equal(3, result.AcceptedCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.All(result.Items, item => Assert.Equal(SyncMutationOutcome.Accepted, item.Outcome));

        // Exactly one effect per workflow, each on the row its own workflow owns.
        Assert.Equal(1, await WeightCountAsync(weightId));
        Assert.Equal(1, await LedgerCountAsync(weightId));

        var attendance = await QueryAsync(db => db.AttendanceRecords
            .AsNoTracking()
            .SingleAsync(r => r.EmployeeId == employeeId));
        Assert.Equal(AttendanceStatus.Present, attendance.Status);
        Assert.Equal(deviceTime, attendance.CheckInAt);
        Assert.Equal(checkInId, attendance.ClientMutationId);
        Assert.Equal(1, await LedgerCountAsync(checkInId));

        var task = await QueryAsync(db => db.FarmTasks
            .AsNoTracking()
            .SingleAsync(t => t.Id == taskId));
        Assert.Equal(FarmTaskStatus.Completed, task.Status);
        Assert.Equal(deviceTime, task.CompletedAt);
        Assert.Equal("Fence repaired", task.CompletionNotes);
        Assert.Equal(completionId, task.CompletionClientMutationId);
        Assert.Equal(1, await LedgerCountAsync(completionId));

        // The kill-mid-flush retry: the same batch, byte for byte, answers byte for byte and
        // writes nothing new.
        var second = await SendAsync(client, _factory.MemberFarmId, items);
        var secondBody = await second.Content.ReadAsStringAsync();

        Assert.Equal(firstBody, secondBody);
        Assert.Equal(1, await WeightCountAsync(weightId));
        Assert.Equal(1, await QueryAsync(db => db.AttendanceRecords
            .CountAsync(r => r.EmployeeId == employeeId)));
        Assert.Equal(1, await LedgerCountAsync(weightId));
        Assert.Equal(1, await LedgerCountAsync(checkInId));
        Assert.Equal(1, await LedgerCountAsync(completionId));
    }

    // ── farm scoping: the service's own check, not a new one ──

    [Fact]
    public async Task ItemNamingAnotherFarmsAnimal_IsRejectedByTheSameCheckAsALiveRequest()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();

        var response = await SendAsync(client, _factory.MemberFarmId,
            WeightItem(mutationId, _factory.ForeignAnimalId, 400m));

        var result = await ReadResultAsync(response);

        // The message is the workflow's own — the same one a live request for that animal id
        // gets — because the sync path calls the same method with the route's farm id.
        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Equal("Animal not found", result.Items[0].Message);
        Assert.Equal(0, await WeightCountAsync(mutationId));
        Assert.Equal(0, await LedgerCountAsync(mutationId));
    }

    // ── message parity with the single-record endpoints ────

    [Fact]
    public async Task WeightValidationFailure_ReportsWhatTheSingleRecordEndpointReports()
    {
        var client = ClientFor(_factory.MemberFarmId);

        var live = await client.PostAsJsonAsync(
            WeightsRoute(_factory.MemberFarmId, _factory.AnimalId), new { weightKg = 0m });
        Assert.Equal(HttpStatusCode.BadRequest, live.StatusCode);
        var liveMessage = await MessageAsync(live);

        var sync = await SendAsync(client, _factory.MemberFarmId,
            WeightItem(Guid.NewGuid(), _factory.AnimalId, 0m));
        var syncMessage = (await ReadResultAsync(sync)).Failures[0].Message;

        Assert.Equal(liveMessage, syncMessage);
    }

    /// <summary>
    /// The message-parity guarantee, on the case that is still a refusal after 4.5.5: the task
    /// is complete and what the device is reporting disagrees with what is recorded. (It used
    /// to be pointed at a completed task with *no* notes, which 4.5.5 deliberately reclassified
    /// from a failure to an already-satisfied outcome — see the tests below.)
    /// </summary>
    [Fact]
    public async Task CompletionWithDifferentNotes_ReportsWhatTheSingleRecordEndpointReports()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var notes = "Rewritten on the device";

        var live = await client.PostAsJsonAsync(
            CompleteRoute(_factory.MemberFarmId, _factory.CompletedTaskId),
            new { completionNotes = notes });
        Assert.Equal(HttpStatusCode.Conflict, live.StatusCode);
        var liveMessage = await MessageAsync(live);

        var sync = await SendAsync(client, _factory.MemberFarmId,
            CompleteItem(Guid.NewGuid(), _factory.CompletedTaskId, completionNotes: notes));
        var result = await ReadResultAsync(sync);

        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Equal(liveMessage, result.Failures[0].Message);
        Assert.Contains("already completed with different completion notes", result.Failures[0].Message);
    }

    [Fact]
    public async Task FutureDeviceTimestamp_IsRefusedWithTheWorkflowsOwnMessage()
    {
        var client = ClientFor(_factory.MemberFarmId);

        var live = await client.PostAsJsonAsync(
            WeightsRoute(_factory.MemberFarmId, _factory.AnimalId),
            new { weightKg = 500m, recordedAt = DateTime.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, live.StatusCode);
        var liveMessage = await MessageAsync(live);

        var mutationId = Guid.NewGuid();
        var sync = await SendAsync(client, _factory.MemberFarmId,
            WeightItem(mutationId, _factory.AnimalId, 500m, DateTime.UtcNow.AddDays(1)));
        var result = await ReadResultAsync(sync);

        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Equal(liveMessage, result.Failures[0].Message);
        Assert.Equal("Recorded date cannot be in the future", result.Failures[0].Message);
        Assert.Equal(0, await WeightCountAsync(mutationId));
    }

    // ── attendance: device time, and "earliest wins" ───────

    [Fact]
    public async Task QueuedCheckIn_IsRecordedOnTheDayTheDeviceSaid()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow.AddDays(-1).AddHours(-3);

        var response = await SendAsync(client, _factory.MemberFarmId,
            CheckInItem(mutationId, _factory.AttendanceEmployeeId, occurredAt));
        var result = await ReadResultAsync(response);

        Assert.Equal(SyncMutationOutcome.Accepted, result.Items[0].Outcome);

        var record = await QueryAsync(db => db.AttendanceRecords
            .AsNoTracking()
            .SingleAsync(r => r.ClientMutationId == mutationId));

        Assert.Equal(occurredAt.Date, record.Date);
        Assert.Equal(occurredAt, record.CheckInAt);
        Assert.Equal(occurredAt, result.Items[0].Result!.Value.GetProperty("checkInAt").GetDateTime());
    }

    [Fact]
    public async Task CheckInForADayThatAlreadyHasARecord_IsSuperseded_AndKeepsTheServersMessage()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var first = await SendAsync(client, _factory.MemberFarmId,
            CheckInItem(firstId, _factory.CheckInConflictEmployeeId));
        Assert.Equal(SyncMutationOutcome.Accepted, (await ReadResultAsync(first)).Items[0].Outcome);

        // The live endpoint's own wording for the same situation, which the queued item must repeat.
        var live = await client.PostAsJsonAsync(
            CheckInRoute(_factory.MemberFarmId, _factory.CheckInConflictEmployeeId), new { });
        Assert.Equal(HttpStatusCode.Conflict, live.StatusCode);
        var liveMessage = await MessageAsync(live);

        var second = await SendAsync(client, _factory.MemberFarmId,
            CheckInItem(secondId, _factory.CheckInConflictEmployeeId));
        var result = await ReadResultAsync(second);

        // Earliest timestamp wins: the later check-in is superseded rather than an error the
        // user has to resolve, and the message still says what happened.
        Assert.Equal(SyncMutationOutcome.Superseded, result.Items[0].Outcome);
        Assert.Equal(liveMessage, result.Items[0].Message);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.RejectedCount);

        // No second row, and nothing recorded against an item that wrote nothing.
        Assert.Equal(1, await QueryAsync(db => db.AttendanceRecords
            .CountAsync(r => r.EmployeeId == _factory.CheckInConflictEmployeeId)));
        Assert.Equal(0, await LedgerCountAsync(secondId));
    }

    /// <summary>
    /// The queued half of "earliest check-in wins": a device reporting an earlier time than the
    /// stored one moves the stored check-in back, and — because that is a write — it is on the
    /// ledger, so its replay is answered rather than re-derived.
    /// </summary>
    [Fact]
    public async Task EarlierQueuedCheckIn_MovesTheStoredTimeBack_AndIsAccepted()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var day = DateTime.UtcNow.Date.AddDays(-4);

        var first = await SendAsync(client, _factory.MemberFarmId,
            CheckInItem(Guid.NewGuid(), _factory.AttendanceEmployeeId, day.AddHours(10)));
        Assert.Equal(SyncMutationOutcome.Accepted, (await ReadResultAsync(first)).Items[0].Outcome);

        var rewindingId = Guid.NewGuid();
        var second = await SendAsync(client, _factory.MemberFarmId,
            CheckInItem(rewindingId, _factory.AttendanceEmployeeId, day.AddHours(7)));
        var result = await ReadResultAsync(second);

        Assert.Equal(SyncMutationOutcome.Accepted, result.Items[0].Outcome);

        var record = await QueryAsync(db => db.AttendanceRecords
            .AsNoTracking()
            .SingleAsync(r => r.EmployeeId == _factory.AttendanceEmployeeId && r.Date == day));
        Assert.Equal(day.AddHours(7), record.CheckInAt);

        // One row throughout, and the rewind is remembered because it changed something.
        Assert.Equal(1, await QueryAsync(db => db.AttendanceRecords
            .CountAsync(r => r.EmployeeId == _factory.AttendanceEmployeeId && r.Date == day)));
        Assert.Equal(1, await LedgerCountAsync(rewindingId));
    }

    /// <summary>
    /// The replay criterion for check-in: the second send writes no second row and is answered
    /// with the first send's own result, byte for byte.
    /// </summary>
    [Fact]
    public async Task CheckInBatchSentTwice_CreatesOneRowAndReturnsTheOriginalResult()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow.Date.AddDays(-2).AddHours(6);
        var item = CheckInItem(mutationId, _factory.AttendanceEmployeeId, occurredAt);

        var first = await SendAsync(client, _factory.MemberFarmId, item);
        var firstResult = await ReadResultAsync(first);
        Assert.Equal(SyncMutationOutcome.Accepted, firstResult.Items[0].Outcome);

        var second = await SendAsync(client, _factory.MemberFarmId, item);
        var secondResult = await ReadResultAsync(second);

        Assert.Equal(SyncMutationOutcome.Accepted, secondResult.Items[0].Outcome);
        Assert.Equal(firstResult.Items[0].TargetEntityId, secondResult.Items[0].TargetEntityId);
        Assert.Equal(
            firstResult.Items[0].Result!.Value.GetRawText(),
            secondResult.Items[0].Result!.Value.GetRawText());

        Assert.Equal(1, await QueryAsync(db => db.AttendanceRecords
            .CountAsync(r => r.EmployeeId == _factory.AttendanceEmployeeId && r.Date == occurredAt.Date)));
        Assert.Equal(1, await LedgerCountAsync(mutationId));
    }

    // ── attendance: check-out, the mirror rule ─────────────

    /// <summary>
    /// Latest check-out wins for a device's own report: the row records when the shift actually
    /// ended, so a later queued time moves it forward and an earlier one leaves it standing as
    /// an already-satisfied intent rather than an error.
    /// </summary>
    [Fact]
    public async Task LaterQueuedCheckOut_MovesTheStoredTimeForward_AndAnEarlierOneIsSuperseded()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var day = DateTime.UtcNow.Date.AddDays(-6);
        var employeeId = _factory.AttendanceEmployeeId;

        await SendAsync(client, _factory.MemberFarmId,
            CheckInItem(Guid.NewGuid(), employeeId, day.AddHours(6)));
        var first = await SendAsync(client, _factory.MemberFarmId,
            CheckOutItem(Guid.NewGuid(), employeeId, day.AddHours(15)));
        Assert.Equal(SyncMutationOutcome.Accepted, (await ReadResultAsync(first)).Items[0].Outcome);

        var later = await SendAsync(client, _factory.MemberFarmId,
            CheckOutItem(Guid.NewGuid(), employeeId, day.AddHours(18)));
        Assert.Equal(SyncMutationOutcome.Accepted, (await ReadResultAsync(later)).Items[0].Outcome);

        var earlierId = Guid.NewGuid();
        var earlier = await SendAsync(client, _factory.MemberFarmId,
            CheckOutItem(earlierId, employeeId, day.AddHours(15)));
        var earlierResult = await ReadResultAsync(earlier);

        Assert.Equal(SyncMutationOutcome.Superseded, earlierResult.Items[0].Outcome);
        Assert.Equal("Employee has already checked out today", earlierResult.Items[0].Message);
        Assert.Equal(0, await LedgerCountAsync(earlierId));

        var record = await QueryAsync(db => db.AttendanceRecords
            .AsNoTracking()
            .SingleAsync(r => r.EmployeeId == employeeId && r.Date == day));
        Assert.Equal(day.AddHours(18), record.CheckOutAt);
    }

    [Fact]
    public async Task CheckOutBatchSentTwice_MovesTheStoredTimeOnce_AndReturnsTheOriginalResult()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var day = DateTime.UtcNow.Date.AddDays(-5);
        var employeeId = _factory.AttendanceEmployeeId;

        await SendAsync(client, _factory.MemberFarmId,
            CheckInItem(Guid.NewGuid(), employeeId, day.AddHours(6)));

        var mutationId = Guid.NewGuid();
        var item = CheckOutItem(mutationId, employeeId, day.AddHours(16));

        var first = await SendAsync(client, _factory.MemberFarmId, item);
        var firstResult = await ReadResultAsync(first);
        Assert.Equal(SyncMutationOutcome.Accepted, firstResult.Items[0].Outcome);

        var second = await SendAsync(client, _factory.MemberFarmId, item);
        var secondResult = await ReadResultAsync(second);

        Assert.Equal(SyncMutationOutcome.Accepted, secondResult.Items[0].Outcome);
        Assert.Equal(firstResult.Items[0].TargetEntityId, secondResult.Items[0].TargetEntityId);
        Assert.Equal(
            firstResult.Items[0].Result!.Value.GetRawText(),
            secondResult.Items[0].Result!.Value.GetRawText());

        var record = await QueryAsync(db => db.AttendanceRecords
            .AsNoTracking()
            .SingleAsync(r => r.EmployeeId == employeeId && r.Date == day));
        Assert.Equal(day.AddHours(16), record.CheckOutAt);
        Assert.Equal(1, await LedgerCountAsync(mutationId));
    }

    // ── task completion: the state machine wins, until it doesn't ──

    [Fact]
    public async Task QueuedTaskCompletion_IsRecordedAtTheDeviceTime()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow.AddHours(-5);

        var response = await SendAsync(client, _factory.MemberFarmId,
            CompleteItem(mutationId, _factory.PendingTaskId, occurredAt));
        var result = await ReadResultAsync(response);

        Assert.Equal(SyncMutationOutcome.Accepted, result.Items[0].Outcome);

        var task = await QueryAsync(db => db.FarmTasks
            .AsNoTracking()
            .SingleAsync(t => t.Id == _factory.PendingTaskId));

        Assert.Equal(FarmTaskStatus.Completed, task.Status);
        Assert.Equal(occurredAt, task.CompletedAt);
        Assert.Equal(mutationId, task.CompletionClientMutationId);
    }

    [Fact]
    public async Task RejectedCompletion_IsNotRemembered_SoARetrySucceedsOnceTheTaskIsReopened()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();

        var refused = await SendAsync(client, _factory.MemberFarmId,
            CompleteItem(mutationId, _factory.CancelledTaskId));
        var refusedResult = await ReadResultAsync(refused);

        Assert.Equal(SyncMutationOutcome.Rejected, refusedResult.Items[0].Outcome);
        Assert.Contains("Current status: Cancelled", refusedResult.Items[0].Message);
        Assert.Equal(0, await LedgerCountAsync(mutationId));

        // A manager reopens the task; the device's queued completion is now applicable.
        var reopen = await client.PostAsJsonAsync(
            $"/api/farm/{_factory.MemberFarmId}/tasks/{_factory.CancelledTaskId}/reopen", new { });
        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);

        var retried = await SendAsync(client, _factory.MemberFarmId,
            CompleteItem(mutationId, _factory.CancelledTaskId));
        var retriedResult = await ReadResultAsync(retried);

        Assert.Equal(SyncMutationOutcome.Accepted, retriedResult.Items[0].Outcome);
        Assert.Equal(1, await LedgerCountAsync(mutationId));
    }

    /// <summary>
    /// "Already completed is applied": a queued completion whose intent the task's state already
    /// satisfies is reported as done — counted in <c>successCount</c>, so the device clears it —
    /// rather than as a failure the user has to resolve. The fixture's completed task recorded
    /// no notes and the item carries none, so the two agree.
    /// </summary>
    [Fact]
    public async Task CompletionForAnAlreadyCompletedTask_IsApplied_NotRejected()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();

        var response = await SendAsync(client, _factory.MemberFarmId,
            CompleteItem(mutationId, _factory.CompletedTaskId));
        var result = await ReadResultAsync(response);

        Assert.Equal(SyncMutationOutcome.Superseded, result.Items[0].Outcome);
        Assert.Equal("Task is already completed", result.Items[0].Message);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Empty(result.Failures);

        // Nothing was written, so nothing is remembered — and a retry re-derives the same answer.
        Assert.Equal(0, await LedgerCountAsync(mutationId));

        var task = await QueryAsync(db => db.FarmTasks
            .AsNoTracking()
            .SingleAsync(t => t.Id == _factory.CompletedTaskId));
        Assert.Equal(FarmTaskStatus.Completed, task.Status);
        Assert.Null(task.CompletionClientMutationId);
    }

    /// <summary>
    /// The replay criterion for task completion. The task is created here rather than taken from
    /// the fixture because completing it through sync changes it, and a second test completing
    /// the same seeded task would make both depend on the order xUnit happened to run them in.
    /// </summary>
    [Fact]
    public async Task TaskCompletionBatchSentTwice_AppliesOnce_AndReturnsTheOriginalResult()
    {
        var client = ClientFor(_factory.MemberFarmId);

        var created = await client.PostAsJsonAsync($"/api/farm/{_factory.MemberFarmId}/tasks", new
        {
            title = "Completion sent twice",
            priority = "Medium",
            dueDate = DateTime.UtcNow.AddDays(1)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var taskId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var mutationId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow.AddHours(-6);
        var item = CompleteItem(mutationId, taskId, occurredAt, "Done from the field");

        var first = await SendAsync(client, _factory.MemberFarmId, item);
        var firstResult = await ReadResultAsync(first);
        Assert.Equal(SyncMutationOutcome.Accepted, firstResult.Items[0].Outcome);

        var second = await SendAsync(client, _factory.MemberFarmId, item);
        var secondResult = await ReadResultAsync(second);

        Assert.Equal(SyncMutationOutcome.Accepted, secondResult.Items[0].Outcome);
        Assert.Equal(firstResult.Items[0].TargetEntityId, secondResult.Items[0].TargetEntityId);
        Assert.Equal(
            firstResult.Items[0].Result!.Value.GetRawText(),
            secondResult.Items[0].Result!.Value.GetRawText());
        Assert.Equal(1, await LedgerCountAsync(mutationId));

        var task = await QueryAsync(db => db.FarmTasks
            .AsNoTracking()
            .SingleAsync(t => t.Id == taskId));
        Assert.Equal(FarmTaskStatus.Completed, task.Status);
        Assert.Equal(occurredAt, task.CompletedAt);
        Assert.Equal("Done from the field", task.CompletionNotes);
        Assert.Equal(mutationId, task.CompletionClientMutationId);
    }

    // ── farm context on the new route ──────────────────────

    [Fact]
    public async Task RouteFarmForANonMemberFarm_IsRefused_AndNothingIsApplied()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();

        var response = await SendAsync(client, _factory.ForeignFarmId,
            WeightItem(mutationId, _factory.ForeignAnimalId, 400m));

        _output.WriteLine($"non-member route -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await LedgerCountAsync(mutationId));
    }

    [Fact]
    public async Task RouteFarmDifferentFromTheHeader_IsRefused_EvenForAMemberFarm()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var mutationId = Guid.NewGuid();

        var response = await SendAsync(client, _factory.OtherMemberFarmId,
            WeightItem(mutationId, _factory.AnimalId, 400m));

        _output.WriteLine($"header/route mismatch -> {response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await LedgerCountAsync(mutationId));
    }

    [Fact]
    public async Task HeaderlessRequestForAMemberFarm_IsAllowed()
    {
        var client = ClientFor(null);
        var mutationId = Guid.NewGuid();

        var response = await SendAsync(client, _factory.MemberFarmId,
            WeightItem(mutationId, _factory.AnimalId, 431m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await WeightCountAsync(mutationId));
    }

    // ── the batch is bounded, and malformed items do not poison it ──

    [Fact]
    public async Task MoreItemsThanTheTransportAllows_IsABadRequest()
    {
        var client = ClientFor(_factory.MemberFarmId);

        var items = Enumerable.Range(0, SyncMutationRequest.MaxItemsPerRequest + 1)
            .Select(_ => WeightItem(Guid.NewGuid(), _factory.AnimalId, 400m))
            .ToArray();

        var response = await client.PostAsJsonAsync(
            SyncRoute(_factory.MemberFarmId), new { items });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownOperationAndMissingIdAreRejectedPerItem_BesideAnItemThatSucceeds()
    {
        var client = ClientFor(_factory.MemberFarmId);
        var goodId = Guid.NewGuid();

        var response = await SendAsync(client, _factory.MemberFarmId,
            new { operation = "animal.create", clientMutationId = Guid.NewGuid(), payload = Payload(new { }) },
            new { operation = SyncOperations.WeightRecord, clientMutationId = Guid.Empty, payload = Payload(new { }) },
            WeightItem(goodId, _factory.AnimalId, 402m));

        var result = await ReadResultAsync(response);

        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[0].Outcome);
        Assert.Contains("Unknown operation", result.Items[0].Message);
        Assert.Equal(SyncMutationOutcome.Rejected, result.Items[1].Outcome);
        Assert.Equal("A client mutation id is required", result.Items[1].Message);
        Assert.Equal(SyncMutationOutcome.Accepted, result.Items[2].Outcome);
        Assert.Equal(1, await WeightCountAsync(goodId));
    }
}
