using System.Text.Json;
using FMS.API.Security;
using FMS.Application.Email;
using Microsoft.Extensions.Configuration;

namespace FMS.Domain.Tests;

/// <summary>
/// Committed configuration is what a deployment inherits, so these pin two promises
/// about it:
///
/// <list type="number">
/// <item>merging the SendGrid transport changes no runtime behaviour — the shipped
/// default is the Log transport, with no credentials anywhere in the repository; and</item>
/// <item>the value that would silently break every email is flagged by the guard, so a
/// <c>Frontend:BaseUrl</c> placeholder in Production cannot ride along unnoticed into
/// messages whose links would point nowhere.</item>
/// </list>
///
/// <para>
/// Production now names the GitHub Pages deployment as its only frontend, so the guard
/// stays quiet and the values below also pin the exact origin and base path that the
/// deployed app uses.
/// </para>
/// </summary>
public class EmailConfigurationDriftTests
{
    // ── helpers ───────────────────────────────────────────

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

    private static JsonElement AppSettings(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(RepoFile("src", "FMS.API", fileName))).RootElement;

    private static IEnumerable<string> TrackedConfigFiles() => new[]
    {
        RepoFile("src", "FMS.API", "appsettings.json"),
        RepoFile("src", "FMS.API", "appsettings.Development.json"),
        RepoFile("src", "FMS.API", "appsettings.Production.json"),
        RepoFile("src", "FMS.API", "appsettings.Staging.json"),
        RepoFile(".env.example"),
        RepoFile("docker-compose.yml")
    };

    // ── the default must be inert ─────────────────────────

    [Fact]
    public void AppSettings_Base_DefaultsToTheLogTransport()
    {
        // What makes this subphase safe to merge: with no credentials supplied, every
        // environment keeps the previous behaviour. Turning delivery on is an explicit
        // act (Email__Provider=SendGrid plus a key), not a consequence of the merge.
        var email = AppSettings("appsettings.json").GetProperty("Email");

        Assert.Equal("Log", email.GetProperty("Provider").GetString());
    }

    [Fact]
    public void AppSettings_Development_KeepsTheLogTransport()
    {
        var email = AppSettings("appsettings.Development.json").GetProperty("Email");

        Assert.Equal("Log", email.GetProperty("Provider").GetString());
    }

    [Fact]
    public void AppSettings_Base_ShipsNoCredentials()
    {
        var email = AppSettings("appsettings.json").GetProperty("Email");

        Assert.True(string.IsNullOrWhiteSpace(email.GetProperty("FromAddress").GetString()),
            "appsettings.json must not ship a sender address — it has to be one the provider has verified.");
        Assert.True(string.IsNullOrWhiteSpace(email.GetProperty("SendGrid").GetProperty("ApiKey").GetString()),
            "appsettings.json must not carry an API key. Supply it via Email__SendGrid__ApiKey.");
    }

    [Fact]
    public void NoTrackedConfigFile_ContainsAnythingShapedLikeASendGridKey()
    {
        // SendGrid keys start with "SG." and are bearer credentials for sending as the
        // verified sender. Catching one here is the last line of defence before it is
        // committed and has to be rotated.
        foreach (var file in TrackedConfigFiles())
        {
            var text = File.ReadAllText(file);

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();

                // Documentation may name the prefix; a value may not use it.
                var looksLikeAValue =
                    (trimmed.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                        || trimmed.Contains("api_key", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("SG.", StringComparison.OrdinalIgnoreCase))
                    && trimmed.Contains("SG.", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.Contains("REPLACE_ME", StringComparison.Ordinal)
                    && !trimmed.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);

                Assert.False(looksLikeAValue,
                    $"{Path.GetFileName(file)} looks like it carries a real SendGrid API key: {trimmed}");
            }
        }
    }

    // ── the template must document how to turn delivery on ──

    [Fact]
    public void EnvExample_DocumentsEverySettingTheGuardRequires()
    {
        var text = File.ReadAllText(RepoFile(".env.example"));

        // A runbook that names a setting the app does not read is worse than none: it
        // sends the operator looking for why nothing changed.
        Assert.Contains("Email__Provider=", text);
        Assert.Contains("Email__FromAddress=", text);
        Assert.Contains("Email__SendGrid__ApiKey=", text);
        Assert.Contains("Frontend__BaseUrl=", text);
        // And the choice has to be explained, not just listed.
        Assert.Contains("Log", text);
        Assert.Contains("SendGrid", text);
        Assert.Contains("REPLACE_ME", text);
    }

    [Fact]
    public void DockerCompose_ForwardsTheEmailSettingsFromDotEnv()
    {
        // Compose only forwards the variables it names. Listing Email__Provider in
        // .env.example while never passing it to the container would look configured and
        // deliver nothing - the same drift class TrackedConfigDriftTests guards for
        // connection strings.
        var compose = File.ReadAllText(RepoFile("docker-compose.yml"));

        Assert.Contains("Email__Provider=${Email__Provider", compose);
        Assert.Contains("Email__FromAddress=${Email__FromAddress", compose);
        Assert.Contains("Email__SendGrid__ApiKey=${Email__SendGrid__ApiKey", compose);
        Assert.Contains("Frontend__BaseUrl=${Frontend__BaseUrl", compose);
    }

    // ── the link target: a flagged precondition ────────────

    [Fact]
    public void AppSettings_Production_FrontendUrl_AndTheGuardAgree()
    {
        // Invariant rather than a fixed expectation: whatever Production ships, the guard's
        // verdict about it must match. Today that value is the GitHub Pages URL, so the
        // guard stays quiet.
        var baseUrl = AppSettings("appsettings.Production.json")
            .GetProperty("Frontend").GetProperty("BaseUrl").GetString();

        var isTemplate = string.IsNullOrWhiteSpace(baseUrl)
            || baseUrl!.Contains("yourdomain.com", StringComparison.OrdinalIgnoreCase);

        var options = new EmailOptions
        {
            Provider = EmailProviderKind.SendGrid,
            FromAddress = "no-reply@example.com",
            SendGrid = { ApiKey = "SG.placeholder-for-this-check-only" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = baseUrl
            })
            .Build();

        var warning = EmailConfigurationGuard.EnsureUsable(
            options, configuration, "Production", isDevelopment: false);

        Assert.Equal(isTemplate, warning is not null);
    }

    [Fact]
    public void AppSettings_Production_FrontendUrlIsTheGitHubPagesSite()
    {
        // Every email link is built from this setting, and Pages serves the app from a repo
        // subpath, so the path is part of the value. No trailing slash: the reset and
        // invitation callers append their own leading slash.
        var baseUrl = AppSettings("appsettings.Production.json")
            .GetProperty("Frontend").GetProperty("BaseUrl").GetString();

        Assert.Equal("https://muhammadabdullah3135.github.io/FarmManagementSystem", baseUrl);
        Assert.DoesNotContain("yourdomain.com", baseUrl!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppSettings_Production_CorsOriginIsTheGitHubPagesOrigin()
    {
        // The origin has no repo path. The retired Netlify frontend must not remain an
        // allowed caller, otherwise it would still be able to reach the production API.
        var origins = AppSettings("appsettings.Production.json")
            .GetProperty("Cors").GetProperty("AllowedOrigins");

        var values = origins.EnumerateArray().Select(o => o.GetString()).ToList();

        Assert.Contains("https://muhammadabdullah3135.github.io", values);
        Assert.DoesNotContain(values, v => v is not null && v.Contains("netlify", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, v => v is not null && v.Contains("yourdomain.com", StringComparison.OrdinalIgnoreCase));
    }

    // ── the interface three features depend on ────────────

    [Fact]
    public void IEmailService_StillHasExactlyTheThreeMethodsCallersUse()
    {
        // The contract the whole subphase promised not to change. If a fourth method
        // appears, it needs a caller and a test — not a quiet default.
        var methods = typeof(FMS.Application.Auth.IEmailService)
            .GetMethods()
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "SendAlertNotificationsEmailAsync",
                "SendFarmInvitationEmailAsync",
                "SendPasswordResetEmailAsync"
            },
            methods);
    }
}
