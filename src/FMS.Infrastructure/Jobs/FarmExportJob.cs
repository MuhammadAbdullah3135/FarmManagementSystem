using System.Diagnostics;
using System.Text.Json;
using FMS.Application.Common;
using FMS.Application.Farm.Export;
using FMS.Application.Jobs;
using FMS.Application.Notifications;
using FMS.Domain.Entities;
using FMS.Infrastructure.Farm.Export;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Jobs;

/// <summary>
/// Builds one farm's export archive: assembles it, stores it, records the outcome on the
/// farm's export row and tells whoever asked that it is ready.
///
/// <para>
/// The only job enqueued per request rather than per schedule, and it follows the same
/// shape as the scheduled ones: one farm per scope (own <c>FmsDbContext</c>, own
/// transaction), its duration logged, and failures logged and rethrown so the scheduler
/// records them and applies the retry policy. A retry re-runs the whole build, which is
/// safe because the archive is written from the farm's current rows and replaces the
/// previous key — there is nothing half-applied to resume.
/// </para>
/// </summary>
public class FarmExportJob
{
    /// <summary>Storage folder for archives. One key per farm, so nothing accumulates.</summary>
    public const string ArchiveFolder = "exports";

    private const string ArchiveExtension = ".zip";

    private readonly FmsDbContext _db;
    private readonly FarmExportAssembler _assembler;
    private readonly IFileStorageService _files;
    private readonly FarmExportOptions _options;
    private readonly ILogger<FarmExportJob> _logger;

    public FarmExportJob(
        FmsDbContext db,
        FarmExportAssembler assembler,
        IFileStorageService files,
        IOptions<FarmExportOptions> options,
        ILogger<FarmExportJob> logger)
    {
        _db = db;
        _assembler = assembler;
        _files = files;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid farmId, Guid exportId)
    {
        var startedAt = Stopwatch.GetTimestamp();

        var export = await _db.FarmExports
            .FirstOrDefaultAsync(row => row.Id == exportId && row.FarmId == farmId);

        if (export is null)
        {
            // Thrown rather than ignored: a job for a row that does not exist means the
            // request that enqueued it and this run disagree about reality, and that is
            // exactly the kind of thing the job-status view exists to surface.
            throw new InvalidOperationException(
                $"Job {JobNames.FarmExport} was asked to build export {exportId}, which does not exist "
                + $"for farm {farmId}.");
        }

        var farmName = await _db.Farms
            .AsNoTracking()
            .Where(farm => farm.Id == farmId)
            .Select(farm => farm.Name)
            .FirstOrDefaultAsync() ?? "farm";

        // Answered with a null: a farm with no name yet is described as such rather than
        // failing the whole export over a display string.
        farmName = string.IsNullOrWhiteSpace(farmName) ? "farm" : farmName;

        var now = DateTime.UtcNow;
        export.Status = FarmExportStatus.Running;
        export.StartedAtUtc = now;
        export.CompletedAtUtc = null;
        export.Error = null;

        // Committed before the build so whoever is watching the status endpoint sees
        // "running" while it runs, rather than a queued row until it finishes.
        await _db.SaveChangesAsync();

        string? temporaryPath = null;

        try
        {
            temporaryPath = Path.Combine(Path.GetTempPath(), $"fms-export-{Guid.NewGuid():N}{ArchiveExtension}");

            var (manifest, storedPath, fileName, sizeBytes) = await BuildAndStoreAsync(
                farmId, farmName, temporaryPath);

            var previousPath = export.StoragePath;

            export.Status = FarmExportStatus.Completed;
            export.CompletedAtUtc = DateTime.UtcNow;
            export.StoragePath = storedPath;
            export.FileName = fileName;
            export.SizeBytes = sizeBytes;
            export.ManifestJson = JsonSerializer.Serialize(manifest);
            export.Error = null;

            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                UserId = export.RequestedByUserId,
                AlertType = NotificationAlertTypes.ExportReady,
                Severity = NotificationSeverity.Info,
                SourceKey = NotificationAlertTypes.SourceKeys.ForExport(export.Id, export.CompletedAtUtc.Value),
                Title = "Your farm export is ready",
                Message = $"The full export of {farmName} is ready to download "
                    + $"({FormatSize(sizeBytes)}, {manifest.TotalRowCount} rows across {manifest.Files.Count} files).",
                // The localisable half of the pair above, stored beside it: the English text is
                // what the digest email sends and what the client falls back to, and the key is
                // what lets the notification centre render this notice in Spanish.
                TitleKey = AlertMessageKeys.ExportReadyTitle,
                TitleArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(("farm", farmName))),
                MessageKey = AlertMessageKeys.ExportReadyMessage,
                MessageArgsJson = MessageArgsJson.Serialize(AlertMessageKeys.Args(
                    ("farm", farmName),
                    ("size", FormatSize(sizeBytes)),
                    ("rows", manifest.TotalRowCount),
                    ("files", manifest.Files.Count))),
                Link = NotificationAlertTypes.ExportReadyLink,
                CreatedAt = export.CompletedAtUtc.Value
            });

            // The record and the notice of it in one save: a row saying "ready" with no
            // notification would leave the requester waiting for a message that never comes.
            await _db.SaveChangesAsync();

            // Only once the new archive is safely stored and recorded. Deleting first would
            // turn a failed upload into a farm with no archive at all.
            if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != storedPath)
            {
                await _files.DeleteAsync(previousPath);
            }

            _logger.LogInformation(
                "Job {JobName} completed for farm {FarmId} in {DurationMs}ms "
                + "({FileCount} files, {RowCount} rows, {SizeBytes} bytes)",
                JobNames.FarmExport,
                farmId,
                Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1),
                manifest.Files.Count,
                manifest.TotalRowCount,
                sizeBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Job {JobName} failed for farm {FarmId}: {ExceptionType} - {ExceptionMessage}",
                JobNames.FarmExport,
                farmId,
                ex.GetType().Name,
                ex.Message);

            export.Status = FarmExportStatus.Failed;
            export.CompletedAtUtc = DateTime.UtcNow;
            export.Error = $"{ex.GetType().Name}: {ex.Message}";

            // A last, best-effort write of the failure. If this itself throws, the
            // original exception still propagates — the scheduler's view of the failure
            // matters more than the row's wording.
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception recordFailure)
            {
                _logger.LogError(
                    recordFailure,
                    "Job {JobName} could not record the failure on export {ExportId} for farm {FarmId}",
                    JobNames.FarmExport,
                    exportId,
                    farmId);
            }

            throw;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Best effort: the OS reclaims the temp directory, and a leftover
                    // staging file must never fail an export that otherwise succeeded.
                }
            }
        }
    }

    /// <summary>
    /// Assembles the archive into a staging file and uploads it.
    ///
    /// <para>
    /// Staged on disk rather than assembled in memory: a farm with years of history is
    /// measured in hundreds of megabytes of CSV, and deflating that through a
    /// <c>MemoryStream</c> is how a job turns into an out-of-memory kill. The staging file
    /// exists because storage validates the content length, which a streaming writer
    /// cannot report until it is done.
    /// </para>
    /// </summary>
    private async Task<(FarmExportManifestDto Manifest, string StoredPath, string FileName, long SizeBytes)>
        BuildAndStoreAsync(Guid farmId, string farmName, string temporaryPath)
    {
        FarmExportManifestDto manifest;
        long sizeBytes;

        await using (var staging = new FileStream(
            temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
        {
            manifest = await _assembler.WriteAsync(
                _db, farmId, farmName, staging, DateTime.UtcNow);

            await staging.FlushAsync();
            sizeBytes = staging.Length;
        }

        var fileName = BuildFileName(farmName, manifest.GeneratedAtUtc);

        await using var upload = new FileStream(
            temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        var saved = await _files.SaveAsync(
            ArchiveFolder,
            upload,
            fileName,
            FarmExportContentType.Zip,
            _options.MaxArchiveBytes,
            [ArchiveExtension]);

        if (!saved.IsSuccess)
        {
            throw new InvalidOperationException($"The archive could not be stored: {saved.Error!.Message}");
        }

        return (manifest, saved.Value!.StoragePath, fileName, sizeBytes);
    }

    /// <summary>
    /// A name a person can recognise in their downloads folder. Sanitised, because it is
    /// built from a user-entered farm name.
    /// </summary>
    private string BuildFileName(string farmName, DateTime generatedAtUtc)
    {
        var date = generatedAtUtc.ToString("yyyy-MM-dd");

        if (!_options.IncludeFarmNameInFileName)
        {
            return $"farm-export-{date}{ArchiveExtension}";
        }

        var slug = new string(farmName
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());

        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));

        return slug.Length == 0
            ? $"farm-export-{date}{ArchiveExtension}"
            : $"{slug}-export-{date}{ArchiveExtension}";
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} bytes"
    };
}
