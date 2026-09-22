using FMS.API.Jobs;
using FMS.Application.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// Builds the scheduler exactly as the API registers it, for the tests that can
/// only reach it through the real container.
///
/// The provider is deliberately kept alive for the rest of the test process.
/// AddHangfire installs process-global state bound to the provider that
/// registered it — a log provider that resolves its loggers through it, and
/// JobStorage.Current — so disposing that provider while Hangfire is still in use
/// anywhere in the process leaves every later Hangfire call throwing
/// ObjectDisposedException("LoggerFactory") out of
/// AspNetCoreLogProvider.GetLogger. That is exactly how this suite went red the
/// first time the PostgreSQL integration tests ran next to it: one class disposed
/// its provider while another was resolving IRecurringJobManager, and the
/// exception replaced the answer those tests were looking for. Building a handful
/// of un-disposed containers costs a test process nothing; a scheduler that
/// breaks its neighbour's does not.
/// </summary>
internal static class SchedulerTestProvider
{
    private static readonly List<ServiceProvider> KeptAlive = new();

    /// <summary>
    /// Registers logging and <see cref="BackgroundJobsSetup.AddFmsBackgroundJobs"/>,
    /// after running <paramref name="configure"/> so a caller can add what the API
    /// adds first (a DbContext, typically).
    /// </summary>
    public static ServiceProvider Build(
        IConfiguration configuration,
        JobOptions options,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddFmsBackgroundJobs(configuration, options);

        var provider = services.BuildServiceProvider();
        KeptAlive.Add(provider);
        return provider;
    }
}
