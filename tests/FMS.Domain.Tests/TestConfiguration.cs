using Microsoft.Extensions.Configuration;

namespace FMS.Domain.Tests;

/// <summary>
/// The configuration services under test are constructed with.
///
/// <para>
/// A handful of services take <see cref="IConfiguration"/> to read settings that end up in
/// things a user keeps — an emailed link, a printed QR label — rather than in behaviour the
/// test is asserting. Those tests still have to supply one, so this is that one: a single
/// frontend origin, spelled once, so a test that cares about the origin asserts against
/// <see cref="FrontendBaseUrl"/> instead of a string literal that could drift from it.
/// </para>
/// </summary>
internal static class TestConfiguration
{
    /// <summary>The origin every QR payload and emailed link is built against in tests.</summary>
    public const string FrontendBaseUrl = "https://fms.test";

    public static IConfiguration WithFrontend(string frontendBaseUrl = FrontendBaseUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = frontendBaseUrl
            })
            .Build();
}
