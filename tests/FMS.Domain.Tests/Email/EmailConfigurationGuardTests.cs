using FMS.API.Security;
using FMS.Application.Email;
using Microsoft.Extensions.Configuration;

namespace FMS.Domain.Tests.Email;

/// <summary>
/// The guard exists because of one specific failure: with <c>Email:Provider = SendGrid</c>
/// but no key or sender, every password reset and invitation is accepted, recorded as
/// sent, and never delivered — and because those two flows deliberately keep working when
/// a send fails (see <see cref="EmailCallerContinuationTests"/>), nothing else surfaces it.
///
/// So: outside Development an unusable provider configuration is a startup failure;
/// inside Development it downgrades to the Log transport with a warning, so a
/// half-configured checkout still runs. The last test keeps the guard wired into
/// startup at all, because deleting that one line would leave every test here passing.
/// </summary>
public class EmailConfigurationGuardTests
{
    private const string ValidAddress = "no-reply@example.com";

    private static EmailOptions SendGridOptions(string? apiKey = "SG.key", string? from = ValidAddress)
    {
        var options = new EmailOptions
        {
            Provider = EmailProviderKind.SendGrid,
            FromAddress = from ?? string.Empty
        };

        options.SendGrid.ApiKey = apiKey ?? string.Empty;
        return options;
    }

    private static IConfiguration Configuration(string? frontendBaseUrl = "https://farm.example.com") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = frontendBaseUrl
            })
            .Build();

    private static string? Ensure(EmailOptions options, bool isDevelopment, string? frontendBaseUrl = "https://farm.example.com") =>
        EmailConfigurationGuard.EnsureUsable(
            options,
            Configuration(frontendBaseUrl),
            isDevelopment ? "Development" : "Production",
            isDevelopment);

    // ── the default: Log ───────────────────────────────────

    [Fact]
    public void EnsureUsable_LogProvider_IsNeverAStartupFailure()
    {
        // The shipped default, and the only configuration a fresh clone has. It is
        // reported (nothing is delivered), never refused: a checkout with no credentials
        // has to keep running.
        var options = new EmailOptions { Provider = EmailProviderKind.Log };

        var failure = Record.Exception(() => Ensure(options, isDevelopment: false));

        Assert.Null(failure);
        Assert.Equal(EmailProviderKind.Log, options.Provider);
    }

    [Fact]
    public void EnsureUsable_LogProviderInDevelopment_SaysNothing()
    {
        // Development writing mail to the log is the expected state, not a defect to warn
        // about on every start.
        var options = new EmailOptions { Provider = EmailProviderKind.Log };

        Assert.Null(Ensure(options, isDevelopment: true));
    }

    [Fact]
    public void EnsureUsable_LogProviderInProduction_WarnsThatNothingIsDelivered()
    {
        var options = new EmailOptions { Provider = EmailProviderKind.Log };

        var warning = Ensure(options, isDevelopment: false);

        Assert.NotNull(warning);
        Assert.Contains("Email:Provider = Log", warning);
        Assert.Contains("password resets", warning);
    }

    // ── an unusable provider refuses to start ──────────────

    [Fact]
    public void EnsureUsable_SendGridWithoutAnApiKey_RefusesToStartOutsideDevelopment()
    {
        var options = SendGridOptions(apiKey: "");

        var error = Assert.Throws<InvalidOperationException>(() => Ensure(options, isDevelopment: false));

        Assert.Contains("Production", error.Message);
        Assert.Contains("Email:SendGrid:ApiKey", error.Message);
        // The operator needs the consequence, not just the setting name.
        Assert.Contains("nobody would receive a password reset", error.Message);
        Assert.Contains("Email__Provider=Log", error.Message);
    }

    [Fact]
    public void EnsureUsable_SendGridWithoutASender_RefusesToStartOutsideDevelopment()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Ensure(SendGridOptions(from: ""), isDevelopment: false));

        Assert.Contains("Email:FromAddress", error.Message);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("no-reply@")]
    [InlineData("@example.com")]
    [InlineData("no-reply@localhost")]
    [InlineData("no-reply @example.com")]
    public void EnsureUsable_SendGridWithAnInvalidSender_RefusesToStartOutsideDevelopment(string address)
    {
        // Providers reject these at send time, which would turn every reset into a
        // silent drop; catching it at startup is the whole point of the guard.
        var error = Assert.Throws<InvalidOperationException>(
            () => Ensure(SendGridOptions(from: address), isDevelopment: false));

        Assert.Contains("not a valid email address", error.Message);
    }

    [Fact]
    public void EnsureUsable_SendGridWithCompleteSettings_ReturnsNoWarning()
    {
        Assert.Null(Ensure(SendGridOptions(), isDevelopment: false));
    }

    // ── Development degrades instead of failing ────────────

    [Fact]
    public void EnsureUsable_SendGridWithoutAnApiKeyInDevelopment_FallsBackToLogWithAWarning()
    {
        var options = SendGridOptions(apiKey: "");

        var warning = Ensure(options, isDevelopment: true);

        Assert.NotNull(warning);
        // Mutated, not merely described: the running app must actually use the Log
        // transport, so a developer can still complete a reset from the log.
        Assert.Equal(EmailProviderKind.Log, options.Provider);
        Assert.Contains("Falling back", warning);
        Assert.Contains("not sent", warning);
    }

    [Fact]
    public void EnsureUsable_SendGridWithoutASenderInDevelopment_AlsoFallsBack()
    {
        var options = SendGridOptions(from: "");

        Assert.NotNull(Ensure(options, isDevelopment: true));
        Assert.Equal(EmailProviderKind.Log, options.Provider);
    }

    [Fact]
    public void EnsureUsable_ValidSendGridSettingsInDevelopment_LeavesTheProviderAlone()
    {
        var options = SendGridOptions();

        Assert.Null(Ensure(options, isDevelopment: true));
        Assert.Equal(EmailProviderKind.SendGrid, options.Provider);
    }

    // ── the link target, which is a precondition for real mail ──

    [Fact]
    public void EnsureUsable_TemplateFrontendUrl_IsFlaggedAsBrokenLinks()
    {
        // appsettings.Production.json still ships this value. Enabling SendGrid with it
        // in place would deliver real emails whose every link points at a host that does
        // not serve the application.
        var warning = Ensure(SendGridOptions(), isDevelopment: false, frontendBaseUrl: "https://yourdomain.com");

        Assert.NotNull(warning);
        Assert.Contains("Frontend:BaseUrl", warning);
        Assert.Contains("yourdomain.com", warning);
    }

    [Fact]
    public void EnsureUsable_MissingFrontendUrl_IsFlagged()
    {
        var warning = Ensure(SendGridOptions(), isDevelopment: false, frontendBaseUrl: null);

        Assert.NotNull(warning);
        Assert.Contains("Frontend:BaseUrl", warning);
        Assert.Contains("localhost:3000", warning);
    }

    [Fact]
    public void EnsureUsable_RealFrontendUrl_IsNotFlagged()
    {
        Assert.Null(Ensure(SendGridOptions(), isDevelopment: false,
            frontendBaseUrl: "https://example.github.io/FarmManagementSystem"));
    }

    // ── the guard must stay wired into startup ─────────────

    [Fact]
    public void Program_CallsTheGuardBeforeBuildingTheHost()
    {
        // Removing the call would quietly restore the silent-drop failure mode while
        // leaving every test above green, which is exactly what happened to this
        // repository's validators (registered, never invoked).
        var program = File.ReadAllText(RepoFile("src", "FMS.API", "Program.cs"));
        var guard = File.ReadAllText(RepoFile("src", "FMS.API", "Security", "EmailConfigurationGuard.cs"));

        Assert.Contains("EmailConfigurationGuard.EnsureUsable(", program);
        // The refusal has to tell the operator what to set, or it is just a crash.
        Assert.Contains("Email__Provider=Log", guard);
        Assert.Contains("Email__FromAddress", guard);
    }

    [Fact]
    public void Program_LogsTheGuardWarning()
    {
        var program = File.ReadAllText(RepoFile("src", "FMS.API", "Program.cs"));

        // A returned warning that is never logged is a guard that does nothing.
        Assert.Contains("emailConfigurationWarning", program);
        Assert.Contains("Email configuration:", program);
    }

    private static string RepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return Path.Combine(new[] { directory!.FullName }.Concat(relativeParts).ToArray());
    }
}
