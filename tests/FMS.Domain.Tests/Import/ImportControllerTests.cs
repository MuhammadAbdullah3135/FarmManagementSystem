using System.Reflection;
using FMS.API.Controllers;
using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The guards in front of the new pipelines: no file, an oversized file and an
/// unreadable mapping are answered with a message the user can act on, before anything
/// is parsed or written — the same behaviour the animal import controller already had,
/// because all three now share it.
///
/// <para>
/// The last test is the empty-diff proof the role surface did not grow: each import
/// controller must carry exactly the authorization of its single-record counterpart,
/// so importing cannot become a way around a role that creating is subject to.
/// </para>
/// </summary>
public class ImportControllerTests
{
    // ── the shared upload guards ───────────────────────────

    [Fact]
    public async Task InventoryPreview_WithNoFile_IsBadRequest()
    {
        var controller = InventoryController();

        Assert.IsType<BadRequestObjectResult>(await controller.Preview(Guid.NewGuid(), null!, null, default));
    }

    [Fact]
    public async Task InventoryPreview_WithAnEmptyFile_IsBadRequest()
    {
        var controller = InventoryController();

        Assert.IsType<BadRequestObjectResult>(
            await controller.Preview(Guid.NewGuid(), FormFile(new byte[0]), null, default));
    }

    [Fact]
    public async Task InventoryPreview_OverTheConfiguredSizeLimit_IsBadRequestAndNeverReachesThePipeline()
    {
        var service = new RecordingInventoryImport();
        var controller = new InventoryImportController(
            service, Options.Create(new ImportOptions { MaxFileBytes = 10 }));

        var result = await controller.Preview(Guid.NewGuid(), FormFile(new byte[50]), null, default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("limit for one import", badRequest.Value!.ToString());
        Assert.False(service.WasCalled);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("not-json-at-all")]
    public async Task InventoryImport_WithAnUnreadableMapping_IsBadRequestRatherThanAnException(string mapping)
    {
        var controller = InventoryController();

        var preview = await controller.Preview(Guid.NewGuid(), FormFile(new byte[20]), mapping, default);
        var commit = await controller.Commit(Guid.NewGuid(), FormFile(new byte[20]), mapping, default);

        Assert.Contains("column mapping", Assert.IsType<BadRequestObjectResult>(preview).Value!.ToString());
        Assert.IsType<BadRequestObjectResult>(commit);
    }

    [Fact]
    public async Task InventoryImport_WithAValidRequest_ReachesThePipeline()
    {
        var service = new RecordingInventoryImport();
        var controller = new InventoryImportController(service, Options.Create(new ImportOptions()));

        var preview = await controller.Preview(Guid.NewGuid(), FormFile(new byte[20]), null, default);
        var commit = await controller.Commit(Guid.NewGuid(), FormFile(new byte[20]), null, default);

        Assert.IsType<OkObjectResult>(preview);
        Assert.IsType<OkObjectResult>(commit);
        Assert.True(service.PreviewCalled);
        Assert.True(service.CommitCalled);
    }

    [Fact]
    public async Task InventoryImport_MapsServiceErrorsToStatusCodes()
    {
        var controller = new InventoryImportController(
            new RecordingInventoryImport { Failure = Error.Validation("bad") },
            Options.Create(new ImportOptions()));

        Assert.IsType<BadRequestObjectResult>(
            await controller.Preview(Guid.NewGuid(), FormFile(new byte[20]), null, default));
    }

    // ── no new role surface ────────────────────────────────

    [Fact]
    public void ImportControllers_CarryTheSameAuthorizationAsTheirSingleRecordCounterpart()
    {
        AssertSameAuthorization<InventoryImportController, InventoryItemsController>();
        AssertSameAuthorization<EmployeeImportController, EmployeesController>();
        AssertSameAuthorization<AnimalImportController, AnimalsController>();
    }

    private static void AssertSameAuthorization<TImport, TSingle>()
    {
        var import = typeof(TImport).GetCustomAttribute<AuthorizeAttribute>();
        var single = typeof(TSingle).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(import);
        Assert.NotNull(single);
        Assert.Equal(single.Roles, import.Roles);
    }

    // ── helpers ─────────────────────────────────────────────

    private static InventoryImportController InventoryController() =>
        new(new RecordingInventoryImport(), Options.Create(new ImportOptions()));

    private static IFormFile FormFile(byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "items.csv");

    private sealed class RecordingInventoryImport : IInventoryImportService
    {
        public bool PreviewCalled { get; private set; }
        public bool CommitCalled { get; private set; }
        public bool WasCalled => PreviewCalled || CommitCalled;

        /// <summary>When set, the preview answers with this failure instead of a report.</summary>
        public Error? Failure { get; set; }

        public Task<Result<ImportPreviewDto>> PreviewAsync(
            Guid farmId, Stream content, string fileName, ImportMapping? mapping, CancellationToken cancellationToken = default)
        {
            PreviewCalled = true;
            return Task.FromResult(Failure is null
                ? Result<ImportPreviewDto>.Success(new ImportPreviewDto())
                : Result<ImportPreviewDto>.Failure(Failure));
        }

        public Task<Result<ImportCommitDto>> CommitAsync(
            Guid farmId, Stream content, string fileName, ImportMapping? mapping, CancellationToken cancellationToken = default)
        {
            CommitCalled = true;
            return Task.FromResult(Result<ImportCommitDto>.Success(new ImportCommitDto()));
        }
    }
}
