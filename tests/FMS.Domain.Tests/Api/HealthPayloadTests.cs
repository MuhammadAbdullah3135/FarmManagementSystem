using System.Text.Json;
using FMS.API.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace FMS.Domain.Tests.Api;

/// <summary>
/// The body of <c>/health</c>, which the post-deploy smoke check reads the deployed commit from.
/// </summary>
/// <remarks>
/// Asserted in-process rather than by booting the API, because startup migrates the database and
/// needs PostgreSQL. This is the producer half: the smoke check asserts the deployed reality.
/// </remarks>
public class HealthPayloadTests
{
    private static JsonElement Serialize(HealthReport report, string sha = "unknown", string environment = "Production") =>
        JsonDocument.Parse(JsonSerializer.Serialize(HealthPayload.For(report, sha, environment))).RootElement;

    private static HealthReport Report(params (string Name, HealthStatus Status)[] entries) =>
        new(
            entries.ToDictionary(
                entry => entry.Name,
                entry => new HealthReportEntry(entry.Status, null, TimeSpan.FromMilliseconds(7), null, null)),
            TimeSpan.FromMilliseconds(9));

    [Fact]
    public void Payload_ReportsTheBuildThatIsAnswering()
    {
        var root = Serialize(Report(("postgresql", HealthStatus.Healthy)), "84cfe81a1b2c", "Production");

        // This is the field the smoke check compares against the commit it released: a release
        // that never reached Heroku is indistinguishable from a good one without it.
        Assert.Equal("84cfe81a1b2c", root.GetProperty("build").GetProperty("sha").GetString());
        Assert.Equal("Production", root.GetProperty("build").GetProperty("environment").GetString());
    }

    [Fact]
    public void Payload_StillCarriesTheCheckResultsItAlwaysDid()
    {
        var root = Serialize(Report(("postgresql", HealthStatus.Healthy), ("disk", HealthStatus.Degraded)));

        Assert.Equal("Degraded", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("checks").GetArrayLength());
        Assert.Equal("postgresql", root.GetProperty("checks")[0].GetProperty("name").GetString());
        Assert.Equal(7, root.GetProperty("checks")[0].GetProperty("duration").GetDouble());
        Assert.Equal(9, root.GetProperty("duration").GetDouble());
    }

    [Fact]
    public void Payload_ReportsAnUnstampedBuildAsUnstamped()
    {
        var root = Serialize(Report(("postgresql", HealthStatus.Healthy)));

        // A locally built image reports "unknown" rather than nothing, so the smoke check can
        // tell "this build was never stamped" from "this build was stamped with something else".
        Assert.Equal("unknown", root.GetProperty("build").GetProperty("sha").GetString());
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
    }
}
