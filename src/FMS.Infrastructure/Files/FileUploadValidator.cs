using FMS.Application.Common;

namespace FMS.Infrastructure.Files;

/// <summary>
/// Upload rules shared by every storage provider, so a file that is rejected on one
/// backend cannot be accepted on another.
/// </summary>
public static class FileUploadValidator
{
    public static Error? Validate(
        long contentLength,
        string originalFileName,
        long maxBytes,
        IReadOnlyCollection<string> allowedExtensions)
    {
        if (contentLength == 0)
            return Error.Validation("File is empty");

        if (contentLength > maxBytes)
            return Error.Validation($"File exceeds the maximum allowed size of {maxBytes / (1024.0 * 1024.0):0.#} MB");

        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !allowedExtensions.Contains(extension.ToLowerInvariant()))
            return Error.Validation(
                $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}");

        return null;
    }

    /// <summary>Lower-cased extension, for building the random stored file name.</summary>
    public static string NormalizedExtension(string originalFileName) =>
        Path.GetExtension(originalFileName).ToLowerInvariant();
}
