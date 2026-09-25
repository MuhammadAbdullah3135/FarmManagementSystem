using System.Text.Json;
using FMS.Application.Common;
using FMS.Application.Farm.Export;
using FMS.Application.Jobs;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Infrastructure.Farm.Export;

/// <inheritdoc />
/// <remarks>
/// This type owns the export's lifecycle and touches no data: the archive itself is built
/// by <see cref="FarmExportJob"/>. The split is what lets the request be answered in one
/// database round trip, which is the whole reason a farm with years of history cannot make
/// the request time out.
/// </remarks>
public class FarmExportService : IFarmExportService
{
    private readonly FmsDbContext _db;
    private readonly IFarmExportQueue _queue;
    private readonly IFileStorageService _files;
    private readonly JobOptions _jobOptions;

    public FarmExportService(
        FmsDbContext db,
        IFarmExportQueue queue,
        IFileStorageService files,
        IOptions<JobOptions> jobOptions)
    {
        _db = db;
        _queue = queue;
        _files = files;
        _jobOptions = jobOptions.Value;
    }

    public async Task<Result<FarmExportDto>> RequestAsync(
        Guid farmId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var farmName = await _db.Farms
            .AsNoTracking()
            .Where(farm => farm.Id == farmId)
            .Select(farm => farm.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (farmName is null)
        {
            return Result<FarmExportDto>.NotFound("Farm not found");
        }

        // Refused rather than accepted: with the job subsystem off, nothing in this
        // process would ever pick the work up, and a request that silently never
        // completes is worse than a request that says so.
        if (!_jobOptions.Enabled)
        {
            return Result<FarmExportDto>.Failure(Error.Unavailable(
                "Full-farm export is unavailable because background jobs are disabled in this deployment "
                + "(Jobs:Enabled = false), so nothing would build the archive. "
                + "Ask an operator to enable the job subsystem."));
        }

        var export = await _db.FarmExports
            .FirstOrDefaultAsync(row => row.FarmId == farmId, cancellationToken);

        var now = DateTime.UtcNow;

        if (export is null)
        {
            export = new FarmExport
            {
                Id = Guid.NewGuid(),
                FarmId = farmId,
                RequestedByUserId = userId,
                Status = FarmExportStatus.Queued,
                CreatedAt = now
            };

            _db.FarmExports.Add(export);
        }
        else
        {
            // Already building: answer with the same export rather than queueing a second
            // archive over the same farm. The window between this check and the insert is
            // not transactional, so two simultaneous requests could still both enqueue;
            // both builds write identical bytes to the same key, which is why that is
            // tolerated rather than locked against.
            if (FarmExportStatus.IsInFlight(export.Status))
            {
                return Result<FarmExportDto>.Success(Map(export));
            }

            // The artifact fields are left alone: until the new build succeeds, the farm
            // keeps the archive it already had, and a failed refresh does not take it away.
            export.Status = FarmExportStatus.Queued;
            export.RequestedByUserId = userId;
            export.StartedAtUtc = null;
            export.CompletedAtUtc = null;
            export.Error = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _queue.Enqueue(farmId, export.Id);

        return Result<FarmExportDto>.Success(Map(export));
    }

    public async Task<Result<FarmExportDto?>> GetLatestAsync(
        Guid farmId,
        CancellationToken cancellationToken = default)
    {
        var export = await _db.FarmExports
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.FarmId == farmId, cancellationToken);

        // Null rather than an error: a farm that has never exported is an ordinary state,
        // not a failed request.
        return Result<FarmExportDto?>.Success(export is null ? null : Map(export));
    }

    public async Task<Result<FarmExportArchive>> OpenArchiveAsync(
        Guid farmId,
        CancellationToken cancellationToken = default)
    {
        var export = await _db.FarmExports
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.FarmId == farmId, cancellationToken);

        if (export?.StoragePath is null)
        {
            return Result<FarmExportArchive>.NotFound(
                "This farm has no export archive yet. Create one and download it when it is ready.");
        }

        // The path is read from the farm's own row, never from the caller, so this cannot
        // be aimed at another farm's file even if a path is supplied with the request.
        if (!await _files.ExistsAsync(export.StoragePath, cancellationToken))
        {
            return Result<FarmExportArchive>.NotFound(
                "The export archive is no longer stored. Create a new export to replace it.");
        }

        return Result<FarmExportArchive>.Success(new FarmExportArchive
        {
            StoragePath = export.StoragePath,
            FileName = export.FileName ?? "farm-export.zip",
            SizeBytes = export.SizeBytes ?? 0
        });
    }

    private static FarmExportDto Map(FarmExport export) => new()
    {
        Id = export.Id,
        FarmId = export.FarmId,
        Status = export.Status,
        RequestedByUserId = export.RequestedByUserId,
        RequestedAtUtc = export.CreatedAt,
        StartedAtUtc = export.StartedAtUtc,
        CompletedAtUtc = export.CompletedAtUtc,
        FileName = export.FileName,
        SizeBytes = export.SizeBytes,
        Error = export.Error,
        IsReady = export.IsReady,
        Manifest = Deserialize(export.ManifestJson)
    };

    /// <summary>
    /// A manifest that cannot be parsed is reported as absent rather than failing the
    /// status call: the archive itself is still there and still downloadable, and losing
    /// the summary is no reason to make the export page look broken.
    /// </summary>
    private static FarmExportManifestDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FarmExportManifestDto>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
