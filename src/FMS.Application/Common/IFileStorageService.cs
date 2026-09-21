namespace FMS.Application.Common;

public class FileStorageInfo
{
    public string StoragePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

/// <summary>
/// A short-lived, scoped URL for reading a single stored object.
/// </summary>
public class PresignedDownloadUrl
{
    public string Url { get; set; } = string.Empty;

    /// <summary>When <see cref="Url"/> stops working, so callers can cache safely.</summary>
    public DateTime ExpiresAt { get; set; }
}

public interface IFileStorageService
{
    Task<Result<FileStorageInfo>> SaveAsync(
        string relativeFolder,
        Stream content,
        string originalFileName,
        string contentType,
        long maxBytes,
        IReadOnlyCollection<string> allowedExtensions);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a short-lived URL that grants read access to exactly one object.
    /// </summary>
    /// <remarks>
    /// The returned URL is a bearer token: anyone holding it can read the object until it
    /// expires. Callers MUST therefore authorize access (farm membership, animal ownership)
    /// BEFORE calling this, and should keep the TTL short. Returns a failed
    /// <see cref="Result{T}"/> when the backend cannot presign; check
    /// <see cref="SupportsPresignedUrls"/> to branch instead of relying on failure.
    /// </remarks>
    Task<Result<PresignedDownloadUrl>> GetPresignedDownloadUrlAsync(
        string storagePath,
        string? downloadFileName = null,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this backend hands out real presigned URLs. False for local disk,
    /// where callers must stream through <see cref="OpenReadAsync"/> instead.
    /// </summary>
    bool SupportsPresignedUrls { get; }
}
