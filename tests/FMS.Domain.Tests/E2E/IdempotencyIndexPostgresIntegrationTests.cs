using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMS.Application.Sync;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The half of phase 5.3's idempotency that only a real database can show.
///
/// <para>
/// The ledger lookup catches a retry that arrives after the first attempt recorded its result.
/// What it cannot catch is a retry that arrives after the mutation was applied but <em>before</em>
/// the ledger row was written — the process died in between, or two syncs raced. There the only
/// thing standing between the device and a duplicate record is the unique index on the mutation id
/// the target row carries, and the EF InMemory provider the rest of the suite runs on ignores
/// unique indexes completely. These tests therefore SKIP without a PostgreSQL server and are the
/// proof that the backstop works.
/// </para>
/// </summary>
public class IdempotencyIndexPostgresIntegrationTests : IClassFixture<PostgresIntegrationFactory>
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresIntegrationFactory _factory;
    private readonly ITestOutputHelper _output;

    public IdempotencyIndexPostgresIntegrationTests(PostgresIntegrationFactory fixture, ITestOutputHelper output)
    {
        _factory = fixture;
        _output = output;
    }

    private HttpClient Client()
    {
        Skip.If(_factory.UnavailableReason is not null, _factory.UnavailableReason);
        _ = _factory.Host;
        return _factory.CreateAuthenticatedClient(_factory.SeedData.FarmId);
    }

    private string SyncRoute => $"/api/farm/{_factory.SeedData.FarmId}/sync/mutations";

    private string WeightsRoute(Guid animalId) =>
        $"/api/farm/{_factory.SeedData.FarmId}/animals/{animalId}/weights";

    private async Task<T> QueryAsync<T>(Func<FmsDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        return await query(db);
    }

    private static object Item(Guid mutationId, Guid animalId, decimal weightKg) => new
    {
        operation = SyncOperations.WeightRecord,
        clientMutationId = mutationId,
        payload = JsonSerializer.SerializeToElement(
            new { animalId, weightKg }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };

    [SkippableFact]
    public async Task AppliedButUnrecordedMutation_IsRefusedByTheUniqueIndex_AndReportedAsAlreadyApplied()
    {
        var client = Client();
        var animalId = await QueryAsync(db => db.Animals
            .Where(a => a.FarmId == _factory.SeedData.FarmId)
            .Select(a => a.Id)
            .FirstAsync());

        var mutationId = Guid.NewGuid();
        var body = new { items = new[] { Item(mutationId, animalId, 480m) } };

        var first = await client.PostAsJsonAsync(SyncRoute, body);
        _output.WriteLine($"first -> {(int)first.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, await QueryAsync(db => db.WeightRecords.CountAsync(w => w.ClientMutationId == mutationId)));

        // The crash window, reproduced exactly: the mutation committed, its ledger row did not.
        var removed = await QueryAsync(db => db.ProcessedMutations
            .Where(m => m.ClientMutationId == mutationId)
            .ExecuteDeleteAsync());
        Assert.Equal(1, removed);

        var retry = await client.PostAsJsonAsync(SyncRoute, body);
        var retryBody = await retry.Content.ReadAsStringAsync();
        _output.WriteLine($"retry -> {(int)retry.StatusCode}: {retryBody}");

        // Not a 500, and not a second measurement: the database refused the duplicate and the
        // endpoint reports that the mutation is already applied.
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        var result = JsonSerializer.Deserialize<SyncMutationResultDto>(retryBody, ReadOptions);
        Assert.NotNull(result);
        Assert.Equal(SyncMutationOutcome.Superseded, result!.Items[0].Outcome);
        Assert.Contains("already applied", result.Items[0].Message);

        Assert.Equal(1, await QueryAsync(db => db.WeightRecords.CountAsync(w => w.ClientMutationId == mutationId)));

        // And the answer is now recorded, so every later retry reads it from the ledger.
        Assert.Equal(1, await QueryAsync(db => db.ProcessedMutations.CountAsync(m => m.ClientMutationId == mutationId)));

        var third = await client.PostAsJsonAsync(SyncRoute, body);
        Assert.Equal(retryBody, await third.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task LiveRecordingsWithoutAMutationId_Coexist_UnderTheFilteredIndex()
    {
        var client = Client();
        var animalId = await QueryAsync(db => db.Animals
            .Where(a => a.FarmId == _factory.SeedData.FarmId)
            .Select(a => a.Id)
            .FirstAsync());

        // Both live writes carry a null mutation id. The index is filtered to non-null values, so
        // the second insert is not a duplicate — without the filter, PostgreSQL would still allow
        // it but SQL Server would not, which is why the filter is spelled out in the configuration.
        var first = await client.PostAsJsonAsync(WeightsRoute(animalId), new { weightKg = 410m });
        var second = await client.PostAsJsonAsync(WeightsRoute(animalId), new { weightKg = 412m });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }
}
