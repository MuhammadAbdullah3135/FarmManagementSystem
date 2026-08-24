namespace FMS.Application.Common;

public class FileStorageInfo
{
    public string StoragePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

public interface IFileStorageService
{
    Task<Result<FileStorageInfo>> SaveAsync(string relativeFolder, Stream content, string originalFileName, string contentType, long maxBytes, IReadOnlyCollection<string> allowedExtensions);
    void Delete(string storagePath);
    bool Exists(string storagePath);
    Stream OpenRead(string storagePath);
}
