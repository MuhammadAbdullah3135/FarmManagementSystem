namespace FMS.Infrastructure.Files;

/// <summary>
/// Bound from configuration (env vars override appsettings), e.g.
/// <c>Storage__Provider=S3</c>, <c>Storage__S3__Bucket=...</c>.
/// No secrets are stored in source; credentials come from the environment.
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    public const string LocalProvider = "Local";
    public const string S3Provider = "S3";

    /// <summary>Local (default, safe for dev/CI) or S3 (any S3-compatible endpoint).</summary>
    public string Provider { get; set; } = LocalProvider;

    /// <summary>Root folder for the Local provider only.</summary>
    public string UploadsRoot { get; set; } = "uploads";

    /// <summary>Lifetime of issued download URLs. Short by design: the URL is a bearer token.</summary>
    public int PresignedUrlTtlMinutes { get; set; } = 5;

    public S3StorageOptions S3 { get; set; } = new();

    public bool IsS3 => string.Equals(Provider, S3Provider, StringComparison.OrdinalIgnoreCase);
}

public class S3StorageOptions
{
    public string? Bucket { get; set; }

    /// <summary>Use "auto" for Cloudflare R2; a real region for AWS S3 or MinIO.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// Custom endpoint (required for R2/MinIO). Leave empty to use real AWS S3.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>R2 and MinIO need path-style addressing.</summary>
    public bool ForcePathStyle { get; set; } = true;

    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(SecretAccessKey);

    public bool IsComplete => !string.IsNullOrWhiteSpace(Bucket) && HasCredentials;
}
