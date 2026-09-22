using System.Net;
using Xunit.Abstractions;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// Date-only query values must reach the services, not be rejected by binding.
/// </summary>
/// <remarks>
/// The SPA's date pickers send <c>YYYY-MM-DD</c>, and the binder is what turns that into a UTC
/// instant. These assert the wiring on the InMemory provider (so they run everywhere, fast);
/// DashboardPostgresIntegrationTests asserts the same URLs against a real database, which is
/// where the Unspecified-kind 400s actually appeared.
/// </remarks>
public class DateQueryBindingE2ETests : IClassFixture<DateQueryBindingE2ETests.Factory>, IDisposable
{
    public class Factory : TestWebApplicationFactory { }

    private readonly Factory _factory;

    public DateQueryBindingE2ETests(Factory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _ = output;
    }

    public void Dispose() { }

    private ApiSeedData.SeedIds Seed => _factory.SeedData ?? throw new InvalidOperationException("Host not initialized");

    private HttpClient GetClient()
    {
        _ = _factory.Host; // ensure host is created and data seeded
        return _factory.CreateAuthenticatedClient(Seed.FarmId);
    }

    public static TheoryData<string> DateOnlyRequests()
    {
        var farm = "{farmId}";
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var year = DateTime.UtcNow.Year;

        return new TheoryData<string>
        {
            $"/api/farm/{farm}/feed/tasks?date={today}&page=1&pageSize=10",
            $"/api/farm/{farm}/tasks?page=1&pageSize=10",
            $"/api/farm/{farm}/finance/reports/monthly-summary?year={year}",
            $"/api/farm/{farm}/finance/reports/expense-breakdown?from=2026-01-01&to=2026-12-31",
            $"/api/farm/{farm}/finance/reports/profit-loss?from=2026-01-01&to=2026-12-31",
            $"/api/farm/{farm}/health/costs/by-month?year={year}",
            $"/api/farm/{farm}/attendance?from=2026-01-01&to=2026-12-31",
            $"/api/farm/{farm}/reports/animals?from=2026-01-01&to=2026-12-31",
            $"/api/farm/{farm}/reports/medical?from=2026-01-01&to=2026-12-31",
        };
    }

    [Theory]
    [MemberData(nameof(DateOnlyRequests))]
    public async Task DateOnlyQueryValues_AreAccepted(string urlTemplate)
    {
        var client = GetClient();
        var url = urlTemplate.Replace("{farmId}", Seed.FarmId.ToString());

        var response = await client.GetAsync(url);

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task AnImpossibleDate_IsRejectedAsABadRequest_RatherThanReachingTheService()
    {
        var client = GetClient();

        var response = await client.GetAsync(
            $"/api/farm/{Seed.FarmId}/feed/tasks?date=not-a-date&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnEmptyDateParameter_IsTreatedAsNoFilter()
    {
        var client = GetClient();

        // Clearing a date picker sends an empty value; that must mean "no filter", not a 400.
        var response = await client.GetAsync(
            $"/api/farm/{Seed.FarmId}/feed/tasks?date=&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
