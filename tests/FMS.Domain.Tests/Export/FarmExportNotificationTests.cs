using FMS.Application.Auth;
using FMS.Application.Common;
using FMS.Application.Dashboard;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Jobs;
using FMS.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// The export's interaction with the notification system, which is the one place a
/// well-intentioned addition would silently break an established invariant.
///
/// <para>
/// <c>NotificationAlertTypes.All</c> is documented as exactly the dashboard's alert set,
/// and two things read it: the preferences matrix, and the dispatcher's auto-resolve set —
/// "open notification whose condition is no longer reported is over". A completion notice
/// in that set would be resolved as cleared on the very next scheduled dispatch, because
/// the dashboard never reports "your export is ready" as a condition. So the type lives
/// outside <c>All</c>, and these tests are what keeps it there.
/// </para>
/// </summary>
public class FarmExportNotificationTests
{
    /// <summary>The dashboard's vocabulary as it stood before this feature.</summary>
    private static readonly string[] DashboardAlertTypes =
    [
        NotificationAlertTypes.OverdueVaccination,
        NotificationAlertTypes.OverdueWeightCheck,
        NotificationAlertTypes.Medicine,
        NotificationAlertTypes.OverdueTask,
        NotificationAlertTypes.DueBirth,
        NotificationAlertTypes.LowInventory
    ];

    [Fact]
    public void ExportReady_IsNotADashboardAlertType()
    {
        Assert.DoesNotContain(NotificationAlertTypes.ExportReady, NotificationAlertTypes.All);
        Assert.False(NotificationAlertTypes.IsKnown(NotificationAlertTypes.ExportReady));
    }

    [Fact]
    public void TheDashboardAlertVocabulary_IsUnchangedByThisFeature()
    {
        // The preferences matrix is built from All, so an entry added here changes what
        // every user sees on the notification-preferences screen. This is the assertion
        // that makes that addition a deliberate act.
        Assert.Equal(
            DashboardAlertTypes.OrderBy(name => name, StringComparer.Ordinal),
            NotificationAlertTypes.All.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ADispatchRun_DoesNotResolveTheExportNotification()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        await harness.BuildAsync(seed.FarmId, Requester());

        // The dashboard reports nothing at all for this farm: no alerts, no failed metrics.
        var dispatcher = new NotificationDispatcher(
            harness.Context,
            new QuietDashboard(),
            new QuietEmail(),
            Options.Create(new NotificationOptions()),
            NullLogger<NotificationDispatcher>.Instance);

        var result = await dispatcher.ExecuteAsync(seed.FarmId);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Resolved);

        // The export notice is still open — a scheduled run must not have closed it.
        var notification = await harness.Context.Notifications
            .Where(row => row.FarmId == seed.FarmId)
            .ToListAsync();

        var exportNotice = Assert.Single(notification);
        Assert.Null(exportNotice.ResolvedAtUtc);
        Assert.Null(exportNotice.ReadAtUtc);
        Assert.Null(exportNotice.DismissedAtUtc);
    }

    [Fact]
    public async Task TheExportNotice_IsAddressedToTheRequesterAlone()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var requester = Guid.NewGuid();
        var other = Guid.NewGuid();

        await harness.BuildAsync(seed.FarmId, requester);

        var all = await harness.Context.Notifications
            .Where(row => row.FarmId == seed.FarmId)
            .ToListAsync();

        Assert.All(all, row => Assert.Equal(requester, row.UserId));
        Assert.DoesNotContain(all, row => row.UserId == other);
    }

    [Fact]
    public async Task EachRequestedExport_NotifiesOnceWithItsOwnIdentity()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var first = await harness.BuildAsync(seed.FarmId, Requester());

        var refresh = await harness.Service.RequestAsync(seed.FarmId, Requester());
        var queued = harness.Queue.Enqueued.Last();
        await harness.Job.ExecuteAsync(queued.FarmId, queued.ExportId);

        var notices = await harness.Context.Notifications
            .Where(row => row.FarmId == seed.FarmId)
            .ToListAsync();

        // Two exports, two notices, each with its own source key: the second is news too,
        // and neither may be deduplicated into the other.
        Assert.Equal(2, notices.Count);
        Assert.Equal(2, notices.Select(row => row.SourceKey).Distinct(StringComparer.Ordinal).Count());
        Assert.All(notices, row => Assert.Null(row.ResolvedAtUtc));
    }

    private static Guid Requester() => Guid.NewGuid();

    /// <summary>A dashboard with no conditions, so the dispatcher has nothing to reconcile.</summary>
    private sealed class QuietDashboard : IDashboardService
    {
        public Task<Result<DashboardSummaryDto>> GetSummaryAsync(Guid farmId) =>
            Task.FromResult(Result<DashboardSummaryDto>.Success(new DashboardSummaryDto()));

        public Task<Result<List<DashboardAlertDto>>> GetAlertsAsync(Guid farmId) =>
            Task.FromResult(Result<List<DashboardAlertDto>>.Success(new List<DashboardAlertDto>()));

        public Task<Result<DashboardAlertSetDto>> ComputeAlertSetAsync(Guid farmId) =>
            Task.FromResult(Result<DashboardAlertSetDto>.Success(new DashboardAlertSetDto()));

        public Task<Result<DashboardChartsDto>> GetChartsAsync(Guid farmId, DateTime? from, DateTime? to) =>
            Task.FromResult(Result<DashboardChartsDto>.Success(new DashboardChartsDto()));
    }

    private sealed class QuietEmail : IEmailService
    {
        public Task SendPasswordResetEmailAsync(string email, string resetLink) => Task.CompletedTask;

        public Task SendFarmInvitationEmailAsync(string email, string farmName, string inviteLink) =>
            Task.CompletedTask;

        public Task SendAlertNotificationsEmailAsync(
            string email,
            string farmName,
            IReadOnlyList<NotificationEmailItem> notifications) => Task.CompletedTask;
    }
}
