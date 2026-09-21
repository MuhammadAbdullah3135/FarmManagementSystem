using Amazon.Runtime;
using Amazon.S3;
using FMS.Infrastructure.Files;

namespace FMS.Domain.Tests;

/// <summary>
/// Presigning is local cryptography — no network call — so these assertions run
/// everywhere, including CI, with no bucket and no credentials.
/// </summary>
public class S3PresignedUrlTests
{
    private const string Bucket = "fms-files";
    private const string Key = "farms/f1/animals/a1/images/abc.jpg";

    private static S3FileStorageService CreateService(TimeSpan? defaultTtl = null)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = "https://account123.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        };

        var client = new AmazonS3Client(new BasicAWSCredentials("test-access-key", "test-secret-key"), config);
        return new S3FileStorageService(client, Bucket, defaultTtl ?? TimeSpan.FromMinutes(5));
    }

    private static Dictionary<string, string> QueryOf(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        return query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : string.Empty));
    }

    [Fact]
    public async Task PresignedUrl_IsAbsoluteAndScopedToTheSingleRequestedObject()
    {
        var service = CreateService();

        var result = await service.GetPresignedDownloadUrlAsync(Key);

        Assert.True(result.IsSuccess);
        var url = result.Value!.Url;

        Assert.StartsWith("https://", url);
        Assert.Contains("test-access-key", QueryOf(url)["X-Amz-Credential"]);
        Assert.Contains("X-Amz-Signature", url);

        // The signature covers the object key, so a different key cannot reuse it.
        var other = await service.GetPresignedDownloadUrlAsync("farms/f1/animals/a1/images/other.jpg");
        Assert.NotEqual(url, other.Value!.Url);
        Assert.Contains("other.jpg", other.Value.Url);
    }

    [Fact]
    public async Task Expiry_MatchesTheRequestedTimeToLive()
    {
        var service = CreateService(defaultTtl: TimeSpan.FromMinutes(5));

        var shortLived = await service.GetPresignedDownloadUrlAsync(Key, null, TimeSpan.FromMinutes(1));
        var defaultLived = await service.GetPresignedDownloadUrlAsync(Key);

        Assert.Equal("60", QueryOf(shortLived.Value!.Url)["X-Amz-Expires"]);
        Assert.Equal("300", QueryOf(defaultLived.Value!.Url)["X-Amz-Expires"]);

        // Reported expiry should track the TTL, not drift from it.
        Assert.InRange(shortLived.Value.ExpiresAt - DateTime.UtcNow, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(61));

        // A shorter window must not produce the same signature as a longer one.
        Assert.NotEqual(
            QueryOf(shortLived.Value.Url)["X-Amz-Signature"],
            QueryOf(defaultLived.Value.Url)["X-Amz-Signature"]);
    }

    [Fact]
    public async Task DownloadFileName_SetsAnAttachmentDisposition_WithoutHeaderInjection()
    {
        var service = CreateService();

        var result = await service.GetPresignedDownloadUrlAsync(Key, "vet report\"\r\nInjected: yes.pdf");

        // Quotes and CR/LF are stripped so the override cannot break out of the header.
        var disposition = QueryOf(result.Value!.Url)["response-content-disposition"];
        Assert.Contains("attachment", disposition);
        Assert.Contains("Injected: yes.pdf", disposition);
        Assert.DoesNotContain("\r", disposition);
        Assert.DoesNotContain("\n", disposition);
        Assert.Equal(2, disposition.Count(c => c == '"'));
    }

    [Fact]
    public async Task Presigning_RejectsMissingKeyAndNonPositiveTtl()
    {
        var service = CreateService();

        Assert.False((await service.GetPresignedDownloadUrlAsync("")).IsSuccess);
        Assert.False((await service.GetPresignedDownloadUrlAsync("  ")).IsSuccess);
        Assert.False((await service.GetPresignedDownloadUrlAsync(Key, null, TimeSpan.Zero)).IsSuccess);
        Assert.False((await service.GetPresignedDownloadUrlAsync(Key, null, TimeSpan.FromMinutes(-5))).IsSuccess);
    }

    [Fact]
    public void S3Storage_AdvertisesPresignedUrlSupport()
    {
        Assert.True(CreateService().SupportsPresignedUrls);
    }

    [Fact]
    public void Factory_RefusesToBuildWithoutBucketOrCredentials()
    {
        var incomplete = new StorageOptions { Provider = "S3", S3 = new S3StorageOptions { Bucket = "b" } };

        // Fail loudly rather than silently falling back to an ephemeral disk.
        Assert.Throws<InvalidOperationException>(() => S3FileStorageService.Create(incomplete));
    }

    [Fact]
    public void Config_UsesPathStyleAndServiceUrlForCustomEndpoints()
    {
        var config = S3FileStorageService.BuildConfig(new S3StorageOptions
        {
            ServiceUrl = "https://account123.r2.cloudflarestorage.com",
            Region = "auto",
            ForcePathStyle = true
        });

        // The SDK normalises the endpoint with a trailing slash.
        Assert.Equal("https://account123.r2.cloudflarestorage.com", config.ServiceURL.TrimEnd('/'));
        Assert.True(config.ForcePathStyle);
        Assert.Equal("auto", config.AuthenticationRegion);
    }
}
