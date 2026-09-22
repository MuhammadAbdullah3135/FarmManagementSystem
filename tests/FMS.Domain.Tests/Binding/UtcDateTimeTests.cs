using System.Globalization;
using System.Text.Json;
using FMS.API.Binding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace FMS.Domain.Tests.Binding;

/// <summary>
/// The rule that a date with no time zone means UTC, and the boundary pieces that apply it.
/// </summary>
/// <remarks>
/// Every date column is <c>timestamp with time zone</c>, which PostgreSQL refuses an
/// Unspecified <see cref="DateTime"/> against — so these inputs used to produce a 400 (query
/// parameters) or a 500 (server-built year boundaries) in production while the InMemory
/// provider the rest of the suite uses returned 200. These tests are the fast, provider-free
/// half of the guard; DashboardPostgresIntegrationTests covers the real database.
/// </remarks>
public class UtcDateTimeTests
{
    // ── Normalize ─────────────────────────────────────────

    [Fact]
    public void Normalize_KeepsTheWallClock_WhenNoZoneWasSpecified()
    {
        var naive = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Unspecified);

        var normalized = UtcDateTime.Normalize(naive);

        // Reading "2026-09-22" as UTC midnight, not as midnight in whatever zone the server
        // happens to run in: shifting the wall clock is how a date filter silently returns
        // the wrong day's rows.
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(naive.Ticks, normalized.Ticks);
    }

    [Fact]
    public void Normalize_LeavesAUtcValueAlone()
    {
        var utc = new DateTime(2026, 9, 22, 10, 30, 0, DateTimeKind.Utc);

        Assert.Equal(utc, UtcDateTime.Normalize(utc));
        Assert.Equal(DateTimeKind.Utc, UtcDateTime.Normalize(utc).Kind);
    }

    [Fact]
    public void Normalize_ConvertsALocalValueToTheSameInstant()
    {
        var local = new DateTime(2026, 9, 22, 10, 30, 0, DateTimeKind.Local);

        var normalized = UtcDateTime.Normalize(local);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(local.ToUniversalTime(), normalized);
    }

    // ── TryParse ──────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-22")]
    [InlineData("2026-09-22T00:00:00")]
    [InlineData("2026-09-22T00:00:00Z")]
    [InlineData("2026-09-22T00:00:00.000Z")]
    public void TryParse_ReadsADateWithNoZone_AsThatSameUtcInstant(string raw)
    {
        var parsed = UtcDateTime.TryParse(raw);

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
        Assert.Equal(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc), parsed.Value);
    }

    [Fact]
    public void TryParse_ConvertsAnExplicitOffset_ToTheSameInstant()
    {
        var parsed = UtcDateTime.TryParse("2026-09-22T05:30:00+05:00");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 9, 22, 0, 30, 0, DateTimeKind.Utc), parsed!.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yesterday")]
    [InlineData("2026-13-45")]
    public void TryParse_RejectsWhatIsNotADate(string? raw)
    {
        Assert.Null(UtcDateTime.TryParse(raw));
    }

    // ── JSON bodies ───────────────────────────────────────

    private sealed record Payload(DateTime When, DateTime? Optional, DateTime? Absent);

    private static JsonSerializerOptions DateOptions()
    {
        var options = new JsonSerializerOptions
        {
            // ASP.NET Core's MVC JSON options are case-insensitive and the SPA sends camelCase
            // bodies, so mirror that rather than STJ's case-sensitive default.
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new UtcNullableDateTimeJsonConverter());
        return options;
    }

    [Fact]
    public void JsonConverter_ReadsADateOnlyString_AsUtcMidnight()
    {
        var payload = JsonSerializer.Deserialize<Payload>(
            """{"when":"2026-09-22","optional":"2026-09-22"}""", DateOptions());

        Assert.NotNull(payload);
        Assert.Equal(DateTimeKind.Utc, payload!.When.Kind);
        Assert.Equal(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc), payload.When);
        Assert.Equal(DateTimeKind.Utc, payload.Optional!.Value.Kind);
    }

    [Fact]
    public void JsonConverter_ReadsNullAndMissingOptionals_WithoutThrowing()
    {
        var payload = JsonSerializer.Deserialize<Payload>(
            """{"when":"2026-09-22T05:00:00Z","optional":null}""", DateOptions());

        Assert.NotNull(payload);
        Assert.Null(payload!.Optional);
        Assert.Null(payload.Absent);
    }

    [Fact]
    public void JsonConverter_WritesAnUnspecifiedValue_WithTheUtcDesignator()
    {
        var json = JsonSerializer.Serialize(
            new Payload(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Unspecified), null, null),
            DateOptions());

        // Without the converter this serialises with no designator at all, which every client
        // then reads as local time — the same ambiguity that started all of this.
        Assert.Contains("2026-09-22T00:00:00Z", json);
    }

    // ── Query binding ─────────────────────────────────────

    private static ModelBindingContext QueryContext(string name, Type modelType, params (string Key, string? Value)[] values)
    {
        var query = new QueryCollection(values.ToDictionary(
            v => v.Key,
            v => new StringValues(v.Value),
            StringComparer.OrdinalIgnoreCase));

        return DefaultModelBindingContext.CreateBindingContext(
            new ActionContext { HttpContext = new DefaultHttpContext() },
            new QueryStringValueProvider(BindingSource.Query, query, CultureInfo.InvariantCulture),
            new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            new BindingInfo(),
            name);
    }

    [Fact]
    public async Task ModelBinder_BindsADateOnlyQueryValue_AsUtc()
    {
        var context = QueryContext("date", typeof(DateTime?), ("date", "2026-09-22"));

        await new UtcDateTimeModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        var bound = Assert.IsType<DateTime>(context.Result.Model);
        Assert.Equal(DateTimeKind.Utc, bound.Kind);
        Assert.Equal(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc), bound);
    }

    [Fact]
    public async Task ModelBinder_LeavesAnAbsentValueUnset_SoOptionalsStayNull()
    {
        var context = QueryContext("from", typeof(DateTime?), ("other", "2026-09-22"));

        await new UtcDateTimeModelBinder().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.Empty(context.ModelState);
    }

    [Fact]
    public async Task ModelBinder_TreatsAnEmptyQueryValue_AsNoFilter()
    {
        var context = QueryContext("to", typeof(DateTime?), ("to", ""));

        await new UtcDateTimeModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Null(context.Result.Model);
    }

    [Fact]
    public async Task ModelBinder_ReportsUnparseableInput_AsAModelError()
    {
        var context = QueryContext("date", typeof(DateTime?), ("date", "not-a-date"));

        await new UtcDateTimeModelBinder().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        Assert.True(context.ModelState.ContainsKey("date"));
        Assert.NotEmpty(context.ModelState["date"]!.Errors);
    }
}
