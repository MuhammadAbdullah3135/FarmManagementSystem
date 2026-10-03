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
            return Error.Validation(
                $"File exceeds the maximum allowed size of {maxBytes / (1024.0 * 1024.0):0.#} MB",
                DomainMessageKeys.FileTooLarge,
                // The megabyte figure goes as a number, not as the `:0.#` string above, so the
                // client can write it in the reader's language. The `:0.#` in the English is
                // kept only so the message stays byte-identical for an English client.
                new Dictionary<string, object?> { ["max"] = maxBytes / (1024.0 * 1024.0) });

        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !allowedExtensions.Contains(extension.ToLowerInvariant()))
            return Error.Validation(
                $"File type '{extension}' is not allowed. Allowed types: {string.Join(", ", allowedExtensions)}",
                DomainMessageKeys.FileTypeNotAllowed,
                // The allowed extensions travel as a list, not as a joined string, so the
                // client can join them the way its language joins a list rather than
                // reproducing English's ", " in every language.
                new Dictionary<string, object?>
                {
                    ["extension"] = extension,
                    ["allowed"] = allowedExtensions.ToArray(),
                });

        return null;
    }

    /// <summary>Lower-cased extension, for building the random stored file name.</summary>
    public static string NormalizedExtension(string originalFileName) =>
        Path.GetExtension(originalFileName).ToLowerInvariant();
}
