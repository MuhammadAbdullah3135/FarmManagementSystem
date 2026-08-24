using FMS.Application.Common;

namespace FMS.Infrastructure.Files;

public class FileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public FileStorageService(string rootPath)
    {
        _rootPath = rootPath;
    }

    public async Task<Result<FileStorageInfo>> SaveAsync(
        string relativeFolder,
        Stream content,
        string originalFileName,
        string contentType,
        long maxBytes,
        IReadOnlyCollection<string> allowedExtensions)
    {
        if (content.Length == 0)
            return Result<FileStorageInfo>.Validation("File is empty");

        if (content.Length > maxBytes)
            return Result<FileStorageInfo>.Validation($"File exceeds the maximum allowed size of {maxBytes / (1024.0 * 1024.0):0.#} MB");

        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !allowedExtensions.Contains(extension.ToLowerInvariant()))
            return Result<FileStorageInfo>.Validation(
                $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var folderPath = Path.Combine(_rootPath, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folderPath);

        var storagePath = $"{relativeFolder.TrimEnd('/')}/{fileName}";
        var fullPath = Path.Combine(_rootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));

        await using var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream);

        return Result<FileStorageInfo>.Success(new FileStorageInfo
        {
            StoragePath = storagePath,
            FileName = fileName,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            FileSizeBytes = content.Length
        });
    }

    public void Delete(string storagePath)
    {
        try
        {
            var fullPath = Path.Combine(_rootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (IOException)
        {
        }
    }

    public bool Exists(string storagePath)
    {
        return File.Exists(Path.Combine(_rootPath, storagePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public Stream OpenRead(string storagePath)
    {
        return new FileStream(
            Path.Combine(_rootPath, storagePath.Replace('/', Path.DirectorySeparatorChar)),
            FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
