using System.Text;
using FMS.Application.Common;
using FMS.Domain.Entities;
using FMS.Infrastructure.Animals;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

/// <summary>
/// Animal files must never be addressed by a public path again: the DTO either carries a
/// short-lived presigned URL, or falls back to the API's own authorized download route.
/// </summary>
public class AnimalFileStorageTests
{
    private const string PresignedUrl = "https://account123.r2.cloudflarestorage.com/fms-files/key?X-Amz-Signature=abc";

    private static FmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed class FakeFileStorage : IFileStorageService
    {
        public bool SupportsPresignedUrls { get; init; }
        public bool PresignSucceeds { get; init; } = true;
        public List<string> Deleted { get; } = [];

        public Task<Result<FileStorageInfo>> SaveAsync(string relativeFolder, Stream content, string originalFileName, string contentType, long maxBytes, IReadOnlyCollection<string> allowedExtensions)
        {
            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(originalFileName)}";

            return Task.FromResult(Result<FileStorageInfo>.Success(new FileStorageInfo
            {
                StoragePath = $"{relativeFolder}/{fileName}",
                FileName = fileName,
                OriginalFileName = originalFileName,
                ContentType = contentType,
                FileSizeBytes = content.Length
            }));
        }

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            Deleted.Add(storagePath);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("bytes")));

        public Task<Result<PresignedDownloadUrl>> GetPresignedDownloadUrlAsync(string storagePath, string? downloadFileName = null, TimeSpan? timeToLive = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(PresignSucceeds
                ? Result<PresignedDownloadUrl>.Success(new PresignedDownloadUrl
                {
                    Url = PresignedUrl,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                })
                : Result<PresignedDownloadUrl>.Unexpected("not supported"));
    }

    private static async Task<(Guid farmId, Guid animalId)> SeedAnimalAsync(FmsDbContext context)
    {
        var farmId = Guid.NewGuid();
        var animalId = Guid.NewGuid();

        context.Farms.Add(new Farm { Id = farmId, AccountId = Guid.NewGuid(), Name = "Test Farm" });
        context.Animals.Add(new Animal { Id = animalId, FarmId = farmId, Name = "Bess", IsDeleted = false });
        await context.SaveChangesAsync();

        return (farmId, animalId);
    }

    private static async Task<Guid> AddImageAsync(AnimalService service, Guid farmId, Guid animalId)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("image-bytes"));
        var result = await service.AddImageAsync(farmId, animalId, stream, "photo.jpg", "image/jpeg", stream.Length, "caption", isPrimary: true);

        Assert.True(result.IsSuccess);
        return result.Value!.Id;
    }

    [Fact]
    public async Task ImageUrl_IsAPresignedUrl_NotAPublicUploadsPath()
    {
        var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = new AnimalService(context, new FixedCurrentUser(), new FakeFileStorage { SupportsPresignedUrls = true });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("image-bytes"));
        var result = await service.AddImageAsync(farmId, animalId, stream, "photo.jpg", "image/jpeg", stream.Length, null, false);

        Assert.Equal(PresignedUrl, result.Value!.Url);
        Assert.DoesNotContain("/uploads/", result.Value.Url);
    }

    [Fact]
    public async Task ImageUrl_FallsBackToTheAuthorizedRoute_WhenStorageCannotPresign()
    {
        var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = new AnimalService(context, new FixedCurrentUser(), new FakeFileStorage { SupportsPresignedUrls = false });

        var imageId = await AddImageAsync(service, farmId, animalId);
        var images = await service.GetImagesAsync(farmId, animalId);

        var url = images.Value!.Single().Url;
        Assert.DoesNotContain("/uploads/", url);
        Assert.Contains($"/api/farm/{farmId}/animals/{animalId}/images/{imageId}/download", url);
    }

    [Fact]
    public async Task ImageUrl_FallsBackToTheAuthorizedRoute_WhenPresigningFails()
    {
        var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = new AnimalService(context, new FixedCurrentUser(), new FakeFileStorage { SupportsPresignedUrls = true, PresignSucceeds = false });

        var imageId = await AddImageAsync(service, farmId, animalId);
        var images = await service.GetImagesAsync(farmId, animalId);

        Assert.Contains(
            $"/api/farm/{farmId}/animals/{animalId}/images/{imageId}/download",
            images.Value!.Single().Url);
    }

    [Fact]
    public async Task DocumentUrl_IsAPresignedUrl_NotAPublicUploadsPath()
    {
        var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = new AnimalService(context, new FixedCurrentUser(), new FakeFileStorage { SupportsPresignedUrls = true });

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf-bytes"));
        var result = await service.AddDocumentAsync(farmId, animalId, stream, "cert.pdf", "application/pdf", stream.Length, "Health", null);

        Assert.Equal(PresignedUrl, result.Value!.Url);
        Assert.DoesNotContain("/uploads/", result.Value.Url);
    }

    [Fact]
    public async Task DeletingAnImage_RemovesTheStoredObject()
    {
        var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var storage = new FakeFileStorage { SupportsPresignedUrls = true };
        var service = new AnimalService(context, new FixedCurrentUser(), storage);

        var imageId = await AddImageAsync(service, farmId, animalId);
        var storedKey = (await context.AnimalImages.SingleAsync(i => i.Id == imageId)).StoragePath;

        Assert.True((await service.DeleteImageAsync(farmId, animalId, imageId)).IsSuccess);
        Assert.Equal([storedKey], storage.Deleted);
    }

    [Fact]
    public async Task Download_ResolvesFilesOnlyForTheOwningFarm()
    {
        var context = CreateContext();
        var (farmId, animalId) = await SeedAnimalAsync(context);
        var service = new AnimalService(context, new FixedCurrentUser(), new FakeFileStorage { SupportsPresignedUrls = true });

        var imageId = await AddImageAsync(service, farmId, animalId);

        // The authorizing caller gets the storage key...
        var allowed = await service.GetImageForDownloadAsync(farmId, animalId, imageId);
        Assert.True(allowed.IsSuccess);
        Assert.EndsWith(".jpg", allowed.Value!.StoragePath);

        // ...and another farm cannot, which is what gates the presigned URL.
        var denied = await service.GetImageForDownloadAsync(Guid.NewGuid(), animalId, imageId);
        Assert.False(denied.IsSuccess);
    }
}
