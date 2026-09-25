using FMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Export;

/// <summary>
/// Farm isolation, asserted on the archive itself.
///
/// The HTTP layer proves a caller cannot ask for another farm's archive; this proves the
/// other half — that the archive built for farm A contains nothing belonging to farm B,
/// across <em>every</em> file rather than the one table a test happened to think of. Both
/// farms are seeded with the same shape and different markers, so a table that lost its
/// filter would put B's marker in A's file and fail here.
///
/// The two tables worth naming are the ones with no <c>FarmId</c> of their own: breeds
/// belong to an animal type, diet-plan items to a diet plan. An unscoped query over either
/// would export every farm's rows into one archive, which is invisible unless the test
/// looks for it.
/// </summary>
public class FarmExportIsolationTests
{
    [Fact]
    public async Task Export_ContainsOnlyTheRequestingFarmsRows_Everywhere()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var alpha = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var beta = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Beta Farm", FarmExportTestSupport.BetaMarker);

        var dto = await harness.BuildAsync(alpha.FarmId, Guid.NewGuid());
        Assert.Equal(FarmExportStatus.Completed, dto.Status);

        var opened = await harness.Service.OpenArchiveAsync(alpha.FarmId);
        Assert.True(opened.IsSuccess, opened.Error?.Message);

        await using var stream = await harness.Files.OpenReadAsync(opened.Value!.StoragePath);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        var entries = FarmExportTestSupport.ReadEntries(buffer.ToArray());
        var foreign = FarmExportTestSupport.BetaMarker;

        var leaks = new List<string>();

        foreach (var (name, bytes) in entries)
        {
            var text = System.Text.Encoding.UTF8.GetString(bytes);

            if (text.Contains(foreign, StringComparison.OrdinalIgnoreCase))
            {
                leaks.Add(name);
            }
        }

        Assert.True(
            leaks.Count == 0,
            $"Farm Beta's rows appear in Alpha's archive: {string.Join(", ", leaks)}");

        // And the manifest names the farm it belongs to.
        var manifest = FarmExportTestSupport.ReadManifest(buffer.ToArray());
        Assert.Equal(alpha.FarmId, manifest.FarmId);
        Assert.Equal("Alpha Farm", manifest.FarmName);
    }

    [Fact]
    public async Task Export_ScopesTablesThatHaveNoFarmIdOfTheirOwn_ThroughTheirParent()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var alpha = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Beta Farm", FarmExportTestSupport.BetaMarker);

        var dto = await harness.BuildAsync(alpha.FarmId, Guid.NewGuid());
        Assert.Equal(FarmExportStatus.Completed, dto.Status);

        var manifest = dto.Manifest!;

        var breeds = manifest.Files.Single(file => file.FileName == "breeds.csv");
        var dietItems = manifest.Files.Single(file => file.FileName == "diet-plan-items.csv");

        // Two breeds for Alpha: Holstein and Alpha's marker breed. If the query were
        // unscoped, Beta's would make it three.
        Assert.Equal(2, breeds.RowCount);
        Assert.Equal(1, dietItems.RowCount);
    }

    [Fact]
    public async Task Export_IncludesSoftDeletedRows_RatherThanSilentlyDroppingHistory()
    {
        using var harness = new FarmExportTestSupport.Harness();

        var alpha = await FarmExportTestSupport.SeedFarmAsync(
            harness.Context, "Alpha Farm", FarmExportTestSupport.AlphaMarker);

        var doomed = await harness.Context.Animals
            .Where(animal => animal.FarmId == alpha.FarmId)
            .OrderBy(animal => animal.TagNumber)
            .FirstAsync();

        doomed.IsDeleted = true;
        doomed.DeletedAt = DateTime.UtcNow;
        await harness.Context.SaveChangesAsync();

        var dto = await harness.BuildAsync(alpha.FarmId, Guid.NewGuid());

        Assert.Equal(
            alpha.AnimalTags.Count,
            dto.Manifest!.Files.Single(file => file.FileName == "animals.csv").RowCount);
    }
}
