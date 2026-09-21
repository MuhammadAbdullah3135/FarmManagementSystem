using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Notifications;
using Hangfire;
using Hangfire.PostgreSql;

namespace FMS.API.Jobs;

/// <summary>
/// Registration and startup wiring for the scheduled-job subsystem, kept out of
/// <c>Program.cs</c> so the container wiring and the cron configuration are
/// testable (the job store itself is PostgreSQL, so InMemory cannot stand in for
/// it and the storage schema/registration path stays a deployment-time check).
/// </summary>
public static class BackgroundJobsSetup
{
    public static JobOptions ResolveOptions(IConfiguration configuration) =>
        configuration.GetSection(JobOptions.SectionName).Get<JobOptions>() ?? new JobOptions();

    /// <summary>
    /// Registers the job storage, the fan-out/job services and (when
    /// <see cref="JobOptions.Enabled"/>) the Hangfire server.
    ///
    /// Job storage always uses the application's own PostgreSQL connection, with
    /// its tables in a dedicated <c>hangfire</c> schema.
    /// </summary>
    public static IServiceCollection AddFmsBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        JobOptions jobOptions)
    {
        services.Configure<JobOptions>(configuration.GetSection(JobOptions.SectionName));

        services.AddScoped<IFarmDirectory, FarmDirectory>();
        services.AddScoped<FeedingTaskGenerationJob>();
        services.AddScoped<HealthStatusRecalculationJob>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<NotificationDispatchJob>();
        services.AddScoped<FarmJobScheduler>();
        services.AddScoped<IJobStatusProvider, HangfireJobStatusProvider>();

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new AutomaticRetryAttribute { Attempts = Math.Max(0, jobOptions.MaxRetryAttempts) })
            .UsePostgreSqlStorage(
                configuration.GetConnectionString("DefaultConnection")!,
                new PostgreSqlStorageOptions
                {
                    // Hangfire installs its own job-storage schema (there is no EF
                    // migration for it). It is created by Initialize below, after
                    // EF migrations, so the two schema owners never race on a
                    // fresh database.
                    PrepareSchemaIfNecessary = true,
                    SchemaName = "hangfire"
                }));

        if (jobOptions.Enabled)
        {
            services.AddHangfireServer();
        }

        return services;
    }

    /// <summary>
    /// Creates the job-storage schema and registers the recurring jobs.
    /// Must run after EF migrations, and only when jobs are enabled.
    ///
    /// Registering with a stable id on every start is idempotent, so restarts
    /// neither duplicate jobs nor discard schedule changes.
    /// </summary>
    public static void Initialize(IServiceProvider services, JobOptions jobOptions, ILogger logger)
    {
        using (var connection = services.GetRequiredService<JobStorage>().GetConnection())
        {
            // Touching the connection is what triggers the schema preparation.
        }

        var recurringJobs = services.GetRequiredService<IRecurringJobManager>();
        var utcOnly = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };

        if (jobOptions.FeedingTaskGeneration.Enabled)
        {
            recurringJobs.AddOrUpdate<FarmJobScheduler>(
                JobNames.FeedingTaskGenerationFanOut,
                scheduler => scheduler.EnqueueFeedingTaskGenerationAsync(),
                jobOptions.FeedingTaskGeneration.Cron,
                utcOnly);
        }
        else
        {
            recurringJobs.RemoveIfExists(JobNames.FeedingTaskGenerationFanOut);
        }

        if (jobOptions.HealthStatusRecalculation.Enabled)
        {
            recurringJobs.AddOrUpdate<FarmJobScheduler>(
                JobNames.HealthStatusRecalculationFanOut,
                scheduler => scheduler.EnqueueHealthStatusRecalculationAsync(),
                jobOptions.HealthStatusRecalculation.Cron,
                utcOnly);
        }
        else
        {
            recurringJobs.RemoveIfExists(JobNames.HealthStatusRecalculationFanOut);
        }

        if (jobOptions.NotificationDispatch.Enabled)
        {
            recurringJobs.AddOrUpdate<FarmJobScheduler>(
                JobNames.NotificationDispatchFanOut,
                scheduler => scheduler.EnqueueNotificationDispatchAsync(),
                jobOptions.NotificationDispatch.Cron,
                utcOnly);
        }
        else
        {
            recurringJobs.RemoveIfExists(JobNames.NotificationDispatchFanOut);
        }

        logger.LogInformation(
            "Background jobs enabled: {FeedingTaskCron} feed-task generation, {HealthStatusCron} health-status recalculation, "
            + "{NotificationDispatchCron} notification dispatch (UTC).",
            jobOptions.FeedingTaskGeneration.Enabled ? jobOptions.FeedingTaskGeneration.Cron : "disabled",
            jobOptions.HealthStatusRecalculation.Enabled ? jobOptions.HealthStatusRecalculation.Cron : "disabled",
            jobOptions.NotificationDispatch.Enabled ? jobOptions.NotificationDispatch.Cron : "disabled");
    }
}
