using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FMS.Application.Common;

namespace FMS.Infrastructure.Files;

/// <summary>
/// S3-compatible object storage (Cloudflare R2, AWS S3, MinIO, Backblaze B2, ...).
///
/// R2 is the recommended default: it is S3-API compatible, so pointing this at AWS S3
/// or MinIO is a configuration change rather than a code change, and it charges no
/// egress — which matters because farm photos are read far more often than written.
/// </summary>
public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly TimeSpan _defaultTimeToLive;

    public S3FileStorageService(IAmazonS3 client, string bucket, TimeSpan? defaultTimeToLive = null)
    {
        _client = client;
        _bucket = bucket;
        _defaultTimeToLive = defaultTimeToLive ?? TimeSpan.FromMinutes(5);
    }

    public bool SupportsPresignedUrls => true;

    public static AmazonS3Config BuildConfig(S3StorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            // Custom endpoint: R2, MinIO, Backblaze, ... Signing still needs a region.
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "auto" : options.Region;
        }
        else
        {
            // No custom endpoint: real AWS S3.
            config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(
                string.IsNullOrWhiteSpace(options.Region) ? "us-east-1" : options.Region);
        }

        return config;
    }

    public static S3FileStorageService Create(StorageOptions storageOptions)
    {
        var options = storageOptions.S3;

        if (!options.IsComplete)
        {
            // Fail loudly rather than silently writing to a disk that vanishes on restart.
            throw new InvalidOperationException(
                "Storage:Provider is 'S3' but the bucket or credentials are missing. " +
                "Set Storage__S3__Bucket, Storage__S3__AccessKeyId and Storage__S3__SecretAccessKey.");
        }

        var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
        var ttlMinutes = storageOptions.PresignedUrlTtlMinutes > 0 ? storageOptions.PresignedUrlTtlMinutes : 5;

        return new S3FileStorageService(
            new AmazonS3Client(credentials, BuildConfig(options)),
            options.Bucket!,
            TimeSpan.FromMinutes(ttlMinutes));
    }

    public async Task<Result<FileStorageInfo>> SaveAsync(
        string relativeFolder,
        Stream content,
        string originalFileName,
        string contentType,
        long maxBytes,
        IReadOnlyCollection<string> allowedExtensions)
    {
        var validationError = FileUploadValidator.Validate(content.Length, originalFileName, maxBytes, allowedExtensions);
        if (validationError is not null)
            return Result<FileStorageInfo>.Failure(validationError);

        var extension = FileUploadValidator.NormalizedExtension(originalFileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var storagePath = $"{relativeFolder.TrimEnd('/')}/{fileName}";

        try
        {
            await _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = storagePath,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false
            });
        }
        catch (AmazonS3Exception ex)
        {
            return Result<FileStorageInfo>.Unexpected($"Failed to store file: {ex.Message}");
        }

        return Result<FileStorageInfo>.Success(new FileStorageInfo
        {
            StoragePath = storagePath,
            FileName = fileName,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            FileSizeBytes = content.Length
        });
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucket,
                Key = storagePath
            }, cancellationToken);
        }
        catch (AmazonS3Exception)
        {
            // Best-effort: a failed delete must not fail the owning request.
        }
    }

    public async Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucket,
                Key = storagePath
            }, cancellationToken);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucket,
            Key = storagePath
        }, cancellationToken);

        return response.ResponseStream;
    }

    public async Task<Result<PresignedDownloadUrl>> GetPresignedDownloadUrlAsync(
        string storagePath,
        string? downloadFileName = null,
        TimeSpan? timeToLive = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return Result<PresignedDownloadUrl>.Validation("Storage path is required");

        var effectiveTimeToLive = timeToLive ?? _defaultTimeToLive;
        if (effectiveTimeToLive <= TimeSpan.Zero)
            return Result<PresignedDownloadUrl>.Validation("Time to live must be positive");

        var expiresAt = DateTime.UtcNow.Add(effectiveTimeToLive);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = storagePath,
            Verb = HttpVerb.GET,
            Expires = expiresAt
        };

        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            // Force a download and keep the user's original file name.
            request.ResponseHeaderOverrides.ContentDisposition =
                $"attachment; filename=\"{SanitizeHeaderValue(downloadFileName)}\"";
        }

        try
        {
            var url = await _client.GetPreSignedURLAsync(request);

            return Result<PresignedDownloadUrl>.Success(new PresignedDownloadUrl
            {
                Url = url,
                ExpiresAt = expiresAt
            });
        }
        catch (AmazonS3Exception ex)
        {
            return Result<PresignedDownloadUrl>.Unexpected($"Failed to sign download URL: {ex.Message}");
        }
    }

    /// <summary>Strips characters that could break out of the Content-Disposition header.</summary>
    private static string SanitizeHeaderValue(string value) =>
        new(value.Where(c => c != '"' && c != '\\' && c != '\r' && c != '\n').ToArray());
}
