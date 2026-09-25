using FMS.Application.Farm.Export;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Farm.Export;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// The export's lifecycle: one request queues one build, a second request during that
/// build is the same export rather than another one, a disabled job subsystem refuses
/// rather than accepting work nothing will run, and a build that fails keeps the copy the
/// farm already had.
///
/// <para>
/// These tests drive the service and the job directly. The HTTP surface — the farm gate,
/// the roles, the download's authorization — is covered separately in
/// <c>FarmExportE2ETests</c>, because those are the parts a unit test cannot see.
/// </para>
/// </summary>
public class FarmExportServiceTests
{
    private static readonly Guid Requester = Guid.NewGuid();

    [Fact]
    public async Task Request_QueuesExactlyOneBuild_AndRecordsIt()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var result = await harness.Service.RequestAsync(seed.FarmId, Requester);

        Assert.True(result.IsSuccess, result.Error?.Message);

        var dto = result.Value!;
        Assert.Equal(FarmExportStatus.Queued, dto.Status);
        Assert.False(dto.IsReady);
        Assert.Equal(Requester, dto.RequestedByUserId);

        var queued = Assert.Single(harness.Queue.Enqueued);
        Assert.Equal(seed.FarmId, queued.FarmId);
        Assert.Equal(dto.Id, queued.ExportId);

        // The record exists before the build does, so the status endpoint has something
        // to report while the work runs.
        var stored = await harness.Context.FarmExports.SingleAsync(row => row.FarmId == seed.FarmId);
        Assert.Equal(FarmExportStatus.Queued, stored.Status);
        Assert.Null(stored.StoragePath);
    }

    [Fact]
    public async Task Request_WhileABuildIsRunning_AnswersWithTheSameExport()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var first = await harness.Service.RequestAsync(seed.FarmId, Requester);
        Assert.True(first.IsSuccess);

        var second = await harness.Service.RequestAsync(seed.FarmId, Guid.NewGuid());

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Id, second.Value!.Id);

        // One archive, not two: a double-click must not enqueue a second build over the
        // same farm.
        Assert.Single(harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Request_AfterABuild_Completes_RequeuesIt_AndKeepsTheArchive()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var built = await harness.BuildAsync(seed.FarmId, Requester);
        Assert.Equal(FarmExportStatus.Completed, built.Status);

        var refresh = await harness.Service.RequestAsync(seed.FarmId, Requester);

        Assert.True(refresh.IsSuccess);
        Assert.Equal(built.Id, refresh.Value!.Id);
        Assert.Equal(FarmExportStatus.Queued, refresh.Value!.Status);

        // The archive from the previous build is still downloadable while the new one is
        // queued: a refresh must not take away the copy the farm already had.
        Assert.True(refresh.Value!.IsReady);
        Assert.NotNull(refresh.Value!.Manifest);

        var opened = await harness.Service.OpenArchiveAsync(seed.FarmId);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        Assert.Equal(2, harness.Queue.Enqueued.Count);
    }

    [Fact]
    public async Task Request_WhenBackgroundJobsAreDisabled_RefusesRatherThanAcceptingUnrunnableWork()
    {
        using var harness = new FarmExportTestSupport.Harness(
            jobs: new JobOptions { Enabled = false });

        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var result = await harness.Service.RequestAsync(seed.FarmId, Requester);

        Assert.False(result.IsSuccess);
        Assert.Equal(FMS.Application.Common.Error.UnavailableCode, result.Error!.Code);
        Assert.Contains("Jobs:Enabled = false", result.Error!.Message, StringComparison.Ordinal);

        // Nothing enqueued and nothing recorded: a refusal that still queued a job would
        // be worse than the refusal.
        Assert.Empty(harness.Queue.Enqueued);
        Assert.Empty(await harness.Context.FarmExports.ToListAsync());
    }

    [Fact]
    public async Task Request_ForAnUnknownFarm_IsNotFound()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var result = await harness.Service.RequestAsync(Guid.NewGuid(), Requester);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
        Assert.Empty(harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Status_ForAFarmThatNeverExported_IsNullRatherThanAnError()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var result = await harness.Service.GetLatestAsync(seed.FarmId);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Download_BeforeAnyBuild_IsNotFound_WithTheNextStepInTheMessage()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var result = await harness.Service.OpenArchiveAsync(seed.FarmId);

        Assert.False(result.IsSuccess);
        Assert.Equal("NotFound", result.Error!.Code);
        Assert.Contains("Create", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_RecordsTheResult_AndNotifiesThePersonWhoAsked()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var built = await harness.BuildAsync(seed.FarmId, Requester);

        Assert.Equal(FarmExportStatus.Completed, built.Status);
        Assert.NotNull(built.StartedAtUtc);
        Assert.NotNull(built.CompletedAtUtc);
        Assert.True(built.SizeBytes > 0);
        Assert.Null(built.Error);

        // Row counts add up: the manifest's total is the sum of its files, and the files
        // cover every exported table.
        Assert.Equal(FarmExportEntities.Tables.Count, built.Manifest!.Files.Count);
        Assert.Equal(
            built.Manifest.Files.Sum(file => file.RowCount),
            built.Manifest.TotalRowCount);

        // Exactly one notice, addressed to the requester, pointing at the page that
        // offers the download.
        var notification = await harness.Context.Notifications.SingleAsync(
            row => row.FarmId == seed.FarmId);

        Assert.Equal(NotificationAlertTypes.ExportReady, notification.AlertType);
        Assert.Equal(Requester, notification.UserId);
        Assert.Equal(NotificationSeverity.Info, notification.Severity);
        Assert.Equal(NotificationAlertTypes.ExportReadyLink, notification.Link);
        Assert.Null(notification.ResolvedAtUtc);
    }

    [Fact]
    public async Task Build_WritesTheArchiveSomewhereTheNextDownloadCanFindIt()
    {
        using var harness = new FarmExportTestSupport.Harness();
        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        await harness.BuildAsync(seed.FarmId, Requester);

        var opened = await harness.Service.OpenArchiveAsync(seed.FarmId);

        Assert.True(opened.IsSuccess, opened.Error?.Message);
        Assert.True(await harness.Files.ExistsAsync(opened.Value!.StoragePath));
        Assert.EndsWith(".zip", opened.Value!.FileName, StringComparison.Ordinal);
        Assert.Contains(seed.FarmName.Replace(' ', '-'), opened.Value!.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Build_WhenItFails_RecordsTheFailureAndKeepsThePreviousArchive()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var seed = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        // First, a successful build so there is something to lose.
        var good = await harness.BuildAsync(seed.FarmId, Requester);
        Assert.Equal(FarmExportStatus.Completed, good.Status);

        // Then a ceiling of one byte, so the storage step refuses the next archive — a
        // real failure of the real build path rather than a stubbed one.
        harness.ExportOptions.MaxArchiveBytes = 1;

        var refresh = await harness.Service.RequestAsync(seed.FarmId, Requester);
        Assert.True(refresh.IsSuccess);

        var queued = harness.Queue.Enqueued.Last();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Job.ExecuteAsync(queued.FarmId, queued.ExportId));

        var status = await harness.Service.GetLatestAsync(seed.FarmId);
        var dto = status.Value!;

        // The failure is visible and named...
        Assert.Equal(FarmExportStatus.Failed, dto.Status);
        Assert.NotNull(dto.Error);
        Assert.Contains("maximum allowed size", dto.Error, StringComparison.OrdinalIgnoreCase);

        // ...and the archive the farm already had is still there. Losing a working copy
        // because a refresh failed is not a failure mode a backup should have.
        Assert.True(dto.IsReady);
        Assert.NotNull(dto.Manifest);

        var opened = await harness.Service.OpenArchiveAsync(seed.FarmId);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
    }
}
