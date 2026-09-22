using FMS.API.Jobs;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FMS.Domain.Tests.Jobs;

/// <summary>
/// The job store is PostgreSQL, which the InMemory test provider cannot stand in
/// for, so the storage schema and the recurring-job writes remain deployment
/// checks. What *is* testable — and what would otherwise only surface as a
/// startup crash — is the container wiring: the scheduler's own services, the
/// per-farm job entry points Hangfire activates, and whether the server is
/// started at all when jobs are switched off.
/// </summary>
public class BackgroundJobsSetupTests
{
    /// <summary>
    /// Nothing listens on port 1, and this suite only asserts container wiring, so
    /// the job storage must not be able to reach a server even where one exists.
    /// With "Host=localhost;Port=5432" it found the PostgreSQL service CI runs for
    /// the integration tests and spent two Npgsql timeouts trying to install its
    /// schema into fms_test as a user that does not exist. A closed port fails
    /// immediately, and identically, on every machine.
    /// </summary>
    private const string UnreachableConnection =
        "Host=127.0.0.1;Port=1;Database=fms_test;Username=test;Password=test";

    private static IConfiguration Configuration(bool enabled = true) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = UnreachableConnection,
                ["Jobs:Enabled"] = enabled ? "true" : "false",
                ["Jobs:FeedingTaskGeneration:Cron"] = "30 4 * * *",
                ["Jobs:HealthStatusRecalculation:Cron"] = "15 * * * *",
                ["Jobs:NotificationDispatch:Cron"] = "45 * * * *"
            })
            .Build();

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<FmsDbContext>(options =>
            options.UseInMemoryDatabase($"JobsSetup_{Guid.NewGuid()}"));
        return services;
    }

    [Fact]
    public void ResolveOptions_ReadsTheConfiguredCronExpressions()
    {
        var options = BackgroundJobsSetup.ResolveOptions(Configuration());

        Assert.True(options.Enabled);
        Assert.Equal("30 4 * * *", options.FeedingTaskGeneration.Cron);
        Assert.Equal("15 * * * *", options.HealthStatusRecalculation.Cron);
        Assert.Equal("45 * * * *", options.NotificationDispatch.Cron);
    }

    [Fact]
    public void ResolveOptions_WithNoConfiguration_UsesSafeDefaults()
    {
        var options = BackgroundJobsSetup.ResolveOptions(new ConfigurationBuilder().Build());

        // Jobs on, daily feeding tasks at 00:05 UTC, hourly health refresh,
        // retries bounded far below Hangfire's default of 10.
        Assert.True(options.Enabled);
        Assert.True(options.FeedingTaskGeneration.Enabled);
        Assert.Equal("5 0 * * *", options.FeedingTaskGeneration.Cron);
        Assert.True(options.HealthStatusRecalculation.Enabled);
        Assert.Equal("0 * * * *", options.HealthStatusRecalculation.Cron);
        // Notification dispatch is the most frequent job: it is what a person is
        // waiting to be told about.
        Assert.True(options.NotificationDispatch.Enabled);
        Assert.Equal("*/15 * * * *", options.NotificationDispatch.Cron);
        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.Equal(120, options.HealthSnapshot.StalenessMinutes);
    }

    [Fact]
    public void AddFmsBackgroundJobs_RegistersEverythingTheSchedulerNeeds()
    {
        var configuration = Configuration();

        // Not disposed on purpose — see SchedulerTestProvider: the container this
        // builds is the one Hangfire binds its process-global logger and job
        // storage to, and disposing it breaks every later Hangfire use in the
        // process, including other test classes running in parallel.
        var provider = SchedulerTestProvider.Build(
            configuration,
            BackgroundJobsSetup.ResolveOptions(configuration),
            services => services.AddDbContext<FmsDbContext>(options =>
                options.UseInMemoryDatabase($"JobsSetup_{Guid.NewGuid()}")));

        using var scope = provider.CreateScope();

        // Startup registration depends on the recurring-job manager...
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRecurringJobManager>());
        // ...the farm fan-out depends on the job client...
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>());
        // ...and both depend on the job storage built from the app's connection string.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<JobStorage>());

        Assert.IsType<HangfireJobStatusProvider>(scope.ServiceProvider.GetRequiredService<IJobStatusProvider>());
        Assert.IsType<FarmDirectory>(scope.ServiceProvider.GetRequiredService<IFarmDirectory>());
    }

    [Fact]
    public void AddFmsBackgroundJobs_RegistersThePerFarmJobEntryPointsHangfireActivates()
    {
        var services = Services();
        services.AddFmsBackgroundJobs(Configuration(), BackgroundJobsSetup.ResolveOptions(Configuration()));

        // Hangfire resolves each job type per execution from its own scope, so
        // these must be registered (scoped: one farm's work, one DbContext).
        Assert.Contains(services, d =>
            d.ServiceType == typeof(FeedingTaskGenerationJob) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d =>
            d.ServiceType == typeof(HealthStatusRecalculationJob) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d =>
            d.ServiceType == typeof(NotificationDispatchJob) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d =>
            d.ServiceType == typeof(INotificationDispatcher) && d.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, d =>
            d.ServiceType == typeof(FarmJobScheduler) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddFmsBackgroundJobs_StartsTheServerOnlyWhenJobsAreEnabled()
    {
        var enabledServices = Services();
        enabledServices.AddFmsBackgroundJobs(Configuration(), new JobOptions { Enabled = true });

        var disabledServices = Services();
        disabledServices.AddFmsBackgroundJobs(Configuration(false), new JobOptions { Enabled = false });

        Assert.Contains(enabledServices, d => d.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(disabledServices, d => d.ServiceType == typeof(IHostedService));
    }
}
