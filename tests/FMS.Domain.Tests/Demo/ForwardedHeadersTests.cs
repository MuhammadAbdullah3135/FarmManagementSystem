using System.Net;
using FMS.API.Networking;
using FMS.API.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FMS.Domain.Tests.Demo;

/// <summary>
/// Covers the opt-in forwarded-header support. The point of the feature is that the demo
/// rate-limit policies partition per visitor rather than per proxy, so the integration tests
/// drive real requests through the same <c>UseForwardedHeaders</c> + limiter pipeline the API
/// uses, and assert the partition key that results.
/// </summary>
public class ForwardedHeadersTests
{
    private const string ProbePath = "/probe";

    private static IHost BuildHost(bool forwardedHeadersEnabled)
    {
        var builder = new HostBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:Enabled"] = forwardedHeadersEnabled ? "true" : "false",
                    ["ForwardedHeaders:TrustAllProxies"] = "true",
                    ["ForwardedHeaders:ForwardLimit"] = "1",
                });
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    if (ForwardedHeadersSetup.IsEnabled(app.ApplicationServices.GetRequiredService<IConfiguration>()))
                    {
                        app.UseForwardedHeaders(ForwardedHeadersSetup.CreateOptions(
                            app.ApplicationServices.GetRequiredService<IConfiguration>()));
                    }

                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                    {
                        // Returns the address the limiter will partition on.
                        endpoints.MapGet(ProbePath, (HttpContext context) =>
                                context.Connection.RemoteIpAddress?.ToString() ?? "unknown")
                            .RequireRateLimiting(DemoRateLimitPolicies.Login);
                    });
                });
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddRateLimiter(DemoRateLimitPolicies.Add);
                });
            });

        return builder.Start();
    }

    private static async Task<HttpResponseMessage> ProbeAsync(HttpClient client, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ProbePath);
        if (forwardedFor is not null)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Forwarded_client_ip_partitions_the_demo_login_rate_limit()
    {
        using var host = BuildHost(forwardedHeadersEnabled: true);
        var client = host.GetTestClient();

        // The middleware actually replaced RemoteIpAddress.
        var first = await ProbeAsync(client, "198.51.100.7");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("198.51.100.7", await first.Content.ReadAsStringAsync());

        // That address gets exactly its own budget (10/min): the eleventh is refused.
        for (var i = 1; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await ProbeAsync(client, "198.51.100.7")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ProbeAsync(client, "198.51.100.7")).StatusCode);

        // A different visitor behind the same proxy is unaffected — the bug this fixes.
        var otherVisitor = await ProbeAsync(client, "198.51.100.8");
        Assert.Equal(HttpStatusCode.OK, otherVisitor.StatusCode);
        Assert.Equal("198.51.100.8", await otherVisitor.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Forwarded_headers_are_ignored_when_the_feature_is_disabled()
    {
        using var host = BuildHost(forwardedHeadersEnabled: false);
        var client = host.GetTestClient();

        // Without the middleware every request shares one partition, so distinct spoofed
        // addresses still exhaust the same 10/min bucket on the eleventh request.
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await ProbeAsync(client, $"203.0.113.{i + 1}")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await ProbeAsync(client, "203.0.113.99")).StatusCode);
    }

    [Fact]
    public void Trust_all_proxies_clears_the_default_loopback_allowlist()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:TrustAllProxies"] = "true",
        });

        var options = ForwardedHeadersSetup.CreateOptions(configuration);

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownNetworks);
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
    }

    [Fact]
    public void Explicit_trusted_proxies_and_networks_are_added()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:TrustAllProxies"] = "false",
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.1.0.0/16",
            ["ForwardedHeaders:ForwardLimit"] = "2",
        });

        var options = ForwardedHeadersSetup.CreateOptions(configuration);

        Assert.Contains(IPAddress.Parse("10.0.0.5"), options.KnownProxies);
        // Explicit entries augment whatever ASP.NET Core trusts by default.
        Assert.Contains(options.KnownNetworks,
            network => network.Prefix.Equals(IPAddress.Parse("10.1.0.0")) && network.PrefixLength == 16);
        Assert.Equal(2, options.ForwardLimit);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
