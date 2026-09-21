using System.Text.Json;
using FMS.Application.Import;
using FMS.Infrastructure.Import;
using Microsoft.Extensions.Configuration;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The rename this subphase made: one <c>Import</c> section now serves animals,
/// employees and inventory, replacing 3.4's animal-only <c>AnimalImport</c>.
///
/// A configuration rename fails silently by nature — the app starts, the section binds
/// to nothing, and every limit quietly becomes the default. For an installation that had
/// raised <c>MaxRows</c> to accept a large herd register, that is a regression with no
/// error attached to it. These tests are the tripwire for both halves: the legacy keys
/// still take effect, and the documentation names the current spelling.
/// </summary>
public class ImportOptionsBindingTests
{
    // ── the fallback ────────────────────────────────────────

    [Fact]
    public void LegacySection_StillSuppliesEveryLimit_WhenTheNewSectionIsAbsent()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["AnimalImport:MaxRows"] = "20000",
            ["AnimalImport:MaxFileBytes"] = "20971520",
            ["AnimalImport:MaxReportedRows"] = "1000",
            ["AnimalImport:SampleValidRows"] = "25"
        });

        Assert.Equal(20000, options.MaxRows);
        Assert.Equal(20971520, options.MaxFileBytes);
        Assert.Equal(1000, options.MaxReportedRows);
        Assert.Equal(25, options.SampleValidRows);
    }

    [Fact]
    public void NewSection_WinsOverTheLegacyOne_ForEveryKey()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Import:MaxRows"] = "100",
            ["Import:MaxFileBytes"] = "2048",
            ["Import:MaxReportedRows"] = "10",
            ["Import:SampleValidRows"] = "3",
            ["AnimalImport:MaxRows"] = "20000",
            ["AnimalImport:MaxFileBytes"] = "20971520",
            ["AnimalImport:MaxReportedRows"] = "1000",
            ["AnimalImport:SampleValidRows"] = "25"
        });

        Assert.Equal(100, options.MaxRows);
        Assert.Equal(2048, options.MaxFileBytes);
        Assert.Equal(10, options.MaxReportedRows);
        Assert.Equal(3, options.SampleValidRows);
    }

    [Fact]
    public void FallbackAppliesPerKey_SoAMixedConfigurationKeepsBothRaisings()
    {
        // The realistic migration: a deployment raises MaxRows in the new spelling while
        // its other limits are still written the old way.
        var options = Bind(new Dictionary<string, string?>
        {
            ["Import:MaxRows"] = "50000",
            ["AnimalImport:MaxReportedRows"] = "1000"
        });

        Assert.Equal(50000, options.MaxRows);
        Assert.Equal(1000, options.MaxReportedRows);

        // Untouched keys keep the documented defaults rather than becoming zero.
        Assert.Equal(ImportOptions.DefaultMaxFileBytes, options.MaxFileBytes);
        Assert.Equal(10, options.SampleValidRows);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("")]
    public void UnusableLegacyValue_IsIgnoredRatherThanFatal(string value)
    {
        // Refusing to boot over a typo in a deprecated key would be a worse outcome than
        // falling back to the default, which is a sane limit.
        var options = Bind(new Dictionary<string, string?> { ["AnimalImport:MaxRows"] = value });

        Assert.Equal(5000, options.MaxRows);
    }

    [Fact]
    public void NoConfigurationAtAll_LeavesTheDocumentedDefaults()
    {
        var options = Bind(new Dictionary<string, string?>());

        Assert.Equal(5000, options.MaxRows);
        Assert.Equal(ImportOptions.DefaultMaxFileBytes, options.MaxFileBytes);
        Assert.Equal(500, options.MaxReportedRows);
        Assert.Equal(10, options.SampleValidRows);
    }

    // ── the documentation must name the current spelling ────

    [Fact]
    public void EnvExample_DocumentsTheCurrentSectionName()
    {
        var lines = File.ReadLines(RepoFile(".env.example")).ToList();

        Assert.Contains(lines, line => line.StartsWith("Import__MaxRows=", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("Import__MaxFileBytes=", StringComparison.Ordinal));

        // The legacy spelling may be *explained* — the comment above these keys does —
        // but must not be handed to an operator as an assignment, or a fresh deployment
        // would set the deprecated keys and never read the documented ones.
        Assert.DoesNotContain(lines, line => line.StartsWith("AnimalImport__", StringComparison.Ordinal));
    }

    [Fact]
    public void AppSettings_Base_LeavesTheSectionUnset()
    {
        // Deliberate, and the reason the fallback above is worth having: a default in the
        // base configuration would set the keys the fallback tests for, and every
        // deployment still using AnimalImport would silently lose its limits.
        var settings = JsonDocument.Parse(File.ReadAllText(RepoFile("src", "FMS.API", "appsettings.json")));

        Assert.False(settings.RootElement.TryGetProperty(ImportOptions.SectionName, out _),
            "appsettings.json must not define an Import section: the defaults live on " +
            $"{nameof(ImportOptions)} so the legacy {ImportOptions.LegacySectionName} keys still apply.");
    }

    // ── helpers ─────────────────────────────────────────────

    private static ImportOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var options = new ImportOptions();
        configuration.GetSection(ImportOptions.SectionName).Bind(options);
        ImportOptionsBinding.ApplyLegacyFallback(options, configuration);

        return options;
    }

    private static string RepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var path = Path.Combine(new[] { directory!.FullName }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"Expected to find {path}");

        return path;
    }
}
