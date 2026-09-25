using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// One farm's full-data export: the request, the build's outcome and the stored
/// archive it produced.
///
/// <para>
/// Exactly one row per farm (a unique index enforces it): asking for a new export
/// re-queues this row rather than adding another, so storage stays bounded without a
/// retention job and the download endpoint always has one answer to "the farm's
/// export". <see cref="Status"/> describes the <em>build</em>; the artifact fields
/// (<see cref="StoragePath"/>, <see cref="SizeBytes"/>, <see cref="ManifestJson"/>, filled
/// by the last build that succeeded) describe the archive that currently exists. They
/// are kept separately on purpose: a re-export that fails must not take away the copy
/// the farm already had.
/// </para>
///
/// <para>
/// A real table rather than Hangfire's own job state, deliberately. Hangfire's
/// store is neither farm-scoped (a farm-facing endpoint must never read another
/// farm's jobs) nor meant to answer product questions: its completed state expires,
/// and a failed job leaves nothing to show the user. See <see cref="FarmExportStatus"/>.
/// </para>
///
/// Operational bookkeeping rather than user-authored business data: excluded from
/// the audit log, like the job-created notification rows.
/// </summary>
public class FarmExport : BaseEntity, IAuditLogExcluded
{
    public Guid FarmId { get; set; }

    /// <summary>Who asked for it. The completion notification is addressed to this member.</summary>
    public Guid RequestedByUserId { get; set; }

    /// <summary>The build's state: one of <see cref="FarmExportStatus"/>.</summary>
    public string Status { get; set; } = FarmExportStatus.Queued;

    /// <summary>When the job started this build (UTC). Null while queued.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>When this build finished or failed (UTC). Null until then.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Storage key of the archive, resolved through <c>IFileStorageService</c>. Null until built.</summary>
    public string? StoragePath { get; set; }

    /// <summary>Name the archive is offered under, e.g. <c>acme-farm-export-2026-09-24.zip</c>.</summary>
    public string? FileName { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>
    /// The archive's <c>manifest.json</c>, stored verbatim. Kept so the status
    /// endpoint can report row counts and the exclusion list without opening the
    /// archive, and so the client's disclosure is the server's own words rather
    /// than a second copy that can drift.
    /// </summary>
    public string? ManifestJson { get; set; }

    /// <summary>Why the build failed, kept so a failure is diagnosable instead of silent.</summary>
    public string? Error { get; set; }

    public Farm Farm { get; set; } = null!;

    /// <summary>
    /// True when an archive exists and can be downloaded.
    ///
    /// Deliberately about the artifact rather than about <see cref="Status"/>: a build in
    /// progress, or one that failed, does not remove the completed archive from before,
    /// and the farm should be able to fetch that copy meanwhile.
    /// </summary>
    public bool IsReady => StoragePath is not null;
}

/// <summary>
/// The export's lifecycle. Strings rather than an enum so the wire contract and
/// the stored value are the same thing, matching how notification severity and
/// alert types are modelled.
/// </summary>
public static class FarmExportStatus
{
    /// <summary>Accepted and enqueued; no job has picked it up yet.</summary>
    public const string Queued = "Queued";

    /// <summary>The job is assembling the archive.</summary>
    public const string Running = "Running";

    /// <summary>The archive is stored and downloadable.</summary>
    public const string Completed = "Completed";

    /// <summary>The build threw; <see cref="FarmExport.Error"/> says why.</summary>
    public const string Failed = "Failed";

    /// <summary>True while a build is in flight, i.e. a request must not enqueue a second one.</summary>
    public static bool IsInFlight(string status) =>
        status is Queued or Running;

    public static bool IsKnown(string status) =>
        status is Queued or Running or Completed or Failed;
}
