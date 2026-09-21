using FMS.API.Controllers;
using FMS.Application.Animal.Import;
using FMS.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The guards in front of the pipeline: no file, an oversized file and an unreadable
/// mapping are answered with a message the user can act on, before anything is parsed
/// or written.
/// </summary>
public class AnimalImportControllerTests
{
    [Fact]
    public async Task Preview_WithNoFile_IsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Preview(Guid.NewGuid(), null!, null, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Preview_WithAnEmptyFile_IsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Preview(Guid.NewGuid(), FormFile(new byte[0]), null, default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Preview_OverTheConfiguredSizeLimit_IsBadRequestAndNeverReachesThePipeline()
    {
        var service = new RecordingImportService();
        var controller = new AnimalImportController(
            service, Options.Create(new AnimalImportOptions { MaxFileBytes = 10 }));

        var result = await controller.Preview(Guid.NewGuid(), FormFile(new byte[50]), null, default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("limit for one import", badRequest.Value!.ToString());
        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task Preview_WithAnUnreadableMapping_IsBadRequestRatherThanAnException()
    {
        var controller = CreateController();

        var result = await controller.Preview(Guid.NewGuid(), FormFile(new byte[20]), "{not json", default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("column mapping", badRequest.Value!.ToString());
    }

    [Fact]
    public async Task Commit_WithAnUnreadableMapping_IsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.Commit(Guid.NewGuid(), FormFile(new byte[20]), "not-json-at-all", default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static AnimalImportController CreateController() =>
        new(new RecordingImportService(), Options.Create(new AnimalImportOptions()));

    private static IFormFile FormFile(byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "animals.csv");

    /// <summary>Stands in for the pipeline, recording whether it was reached at all.</summary>
    private sealed class RecordingImportService : IAnimalImportService
    {
        public bool WasCalled { get; private set; }

        public Task<Result<AnimalImportPreviewDto>> PreviewAsync(
            Guid farmId, Stream content, string fileName, AnimalImportMapping? mapping, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result<AnimalImportPreviewDto>.Success(new AnimalImportPreviewDto()));
        }

        public Task<Result<AnimalImportCommitDto>> CommitAsync(
            Guid farmId, Stream content, string fileName, AnimalImportMapping? mapping, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(Result<AnimalImportCommitDto>.Success(new AnimalImportCommitDto()));
        }
    }
}
