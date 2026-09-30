using System.Text;
using FMS.Application.Common;
using FMS.Application.Import;
using FMS.Application.Inventory;
using FMS.Application.Inventory.Import;
using FMS.Domain.Entities;
using FMS.Domain.Tests.Jobs;
using FMS.Infrastructure.Import;
using FMS.Infrastructure.Inventory;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FMS.Domain.Tests.Import;

/// <summary>
/// The customer import: column mapping, row validation against the create endpoint's own
/// rules, duplicate handling and the all-or-nothing commit. Customer is the shallowest
/// importer — a name and a contact — so the identifier is the whole story.
/// </summary>
public class CustomerImportServiceTests
{
    private const string FullHeaders = "customer,contact";

    [Fact]
    public async Task Preview_ValidFile_ReportsEveryRowAsValid_AndWritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\n" +
                  "Nairobi Dairy Co-op,+254 711 111111\n" +
                  "Mombasa Hotel,, \n";

        var preview = await harness.Service.PreviewAsync(harness.FarmId, Utf8(csv), "customers.csv", null);

        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value!.ValidRowCount);
        Assert.Equal(0, preview.Value.InvalidRowCount);
        Assert.Empty(preview.Value.Lookups);
        Assert.Equal(0, await harness.Context.Customers.CountAsync());
    }

    [Fact]
    public async Task Preview_ReportsTheFilesHeaders_AndTheSuggestedMapping()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n,\n"), "customers.csv", null);

        var dto = preview.Value!;
        Assert.Equal(FullHeaders.Split(','), dto.Headers);
        Assert.Equal(0, dto.SuggestedMapping.For(CustomerImportFields.Name)!.Column);
        Assert.Equal(1, dto.SuggestedMapping.For(CustomerImportFields.ContactInfo)!.Column);
    }

    [Fact]
    public async Task Commit_ValidFile_CreatesEveryCustomer()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\nNairobi Dairy Co-op,+254 711 111111\nMombasa Hotel,\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "customers.csv", null);

        Assert.Equal(2, commit.Value!.ImportedCount);
        var customers = await harness.Context.Customers.OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(new[] { "Mombasa Hotel", "Nairobi Dairy Co-op" }, customers.Select(c => c.Name));
        Assert.Equal("+254 711 111111", customers[1].ContactInfo);
        Assert.Null(customers[0].ContactInfo);
    }

    [Fact]
    public async Task Commit_ProducesTheSameRecordAsTheCreateEndpoint()
    {
        using var harness = await CreateHarnessAsync();

        await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\nNairobi Dairy Co-op,  contact  \n"), "customers.csv", null);

        var imported = Assert.Single(await harness.Context.Customers.ToListAsync());

        // The single-create path, same values — in another farm, because the name is the
        // identifier and the endpoint would (correctly) refuse it twice in one farm.
        var created = await harness.Customers.CreateCustomerAsync(
            await SeedForeignFarmAsync(harness), new CreateCustomerRequest { Name = "Nairobi Dairy Co-op", ContactInfo = "  contact  " });

        Assert.True(created.IsSuccess);
        Assert.Equal(created.Value!.Name, imported.Name);
        Assert.Equal(created.Value.ContactInfo, imported.ContactInfo);
    }

    [Fact]
    public async Task Commit_DuplicateName_IsRefusedRatherThanSkipped()
    {
        using var harness = await CreateHarnessAsync();
        await SeedExistingCustomerAsync(harness, "Nairobi Dairy Co-op");

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\nNairobi Dairy Co-op,\n"), "customers.csv", null);

        Assert.Equal(0, commit.Value!.ImportedCount);
        Assert.Equal(CustomerRules.DuplicateNameMessage,
            Assert.Single(Assert.Single(commit.Value.InvalidRows).Errors).Message);
        Assert.Equal(1, await harness.Context.Customers.CountAsync());
    }

    [Fact]
    public async Task Commit_DuplicateNameInsideTheFile_WritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\nNairobi Dairy Co-op,\nNairobi Dairy Co-op,\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "customers.csv", null);

        Assert.Equal(0, commit.Value!.ImportedCount);
        var invalid = Assert.Single(commit.Value.InvalidRows);
        Assert.Equal(3, invalid.RowNumber);
        Assert.Contains("Duplicate customer name 'Nairobi Dairy Co-op' in this file", invalid.Errors[0].Message);
        Assert.Equal(0, await harness.Context.Customers.CountAsync());
    }

    [Fact]
    public async Task Commit_WithOneInvalidRow_WritesNothing()
    {
        using var harness = await CreateHarnessAsync();

        var csv = $"{FullHeaders}\nNairobi Dairy Co-op,+254 711 111111\n,orphan contact\n";
        var commit = await harness.Service.CommitAsync(harness.FarmId, Utf8(csv), "customers.csv", null);

        Assert.Equal(2, commit.Value!.TotalRows);
        Assert.Equal(0, commit.Value.ImportedCount);
        Assert.Null(await harness.Context.Customers.FirstOrDefaultAsync(c => c.Name == "Nairobi Dairy Co-op"));
    }

    [Fact]
    public async Task Commit_ReportsTheEndpointsOwnRequiredMessage()
    {
        using var harness = await CreateHarnessAsync();

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n,\n"), "customers.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Customers.CreateCustomerAsync(
            harness.FarmId, new CreateCustomerRequest { Name = "" });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
        Assert.Equal("Customer name is required", message);
    }

    [Fact]
    public async Task Commit_OverlongName_ReportsTheSameCapAsTheEndpoint()
    {
        using var harness = await CreateHarnessAsync();
        var longName = new string('a', CustomerRules.NameMaxLength + 1);

        var commit = await harness.Service.CommitAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n{longName},\n"), "customers.csv", null);
        var message = Assert.Single(Assert.Single(commit.Value!.InvalidRows).Errors).Message;

        var endpoint = await harness.Customers.CreateCustomerAsync(
            harness.FarmId, new CreateCustomerRequest { Name = longName });

        Assert.False(endpoint.IsSuccess);
        Assert.Equal(endpoint.Error!.Message, message);
    }

    [Fact]
    public async Task Preview_AFileWithNoDataRows_IsRefused()
    {
        using var harness = await CreateHarnessAsync();

        var preview = await harness.Service.PreviewAsync(
            harness.FarmId, Utf8($"{FullHeaders}\n"), "customers.csv", null);

        Assert.False(preview.IsSuccess);
        Assert.Contains("no data rows", preview.Error!.Message);
    }

    // ── harness ─────────────────────────────────────────────

    internal sealed class Harness : IDisposable
    {
        public required FmsDbContext Context { get; init; }
        public required Guid FarmId { get; init; }
        public required CustomerImportService Service { get; init; }
        public required ICustomerService Customers { get; init; }
        public required CapturingLoggerProvider Logs { get; init; }

        public void Dispose() => Context.Dispose();
    }

    private static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static async Task<Harness> CreateHarnessAsync(Action<ImportOptions>? configure = null)
    {
        var context = new FmsDbContext(new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Import Farm" };
        context.Farms.Add(farm);
        await context.SaveChangesAsync();

        var (logger, logs) = TestLoggers.Create<CustomerImportService>();
        var customers = new CustomerService(context, new FixedCurrentUser());

        var options = new ImportOptions();
        configure?.Invoke(options);

        return new Harness
        {
            Context = context,
            FarmId = farm.Id,
            Service = new CustomerImportService(new SpreadsheetReader(), customers, Options.Create(options), logger),
            Customers = customers,
            Logs = logs
        };
    }

    private static async Task<Guid> SeedForeignFarmAsync(Harness harness)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Foreign Farm" };
        harness.Context.Farms.Add(farm);
        await harness.Context.SaveChangesAsync();
        return farm.Id;
    }

    private static async Task SeedExistingCustomerAsync(Harness harness, string name)
    {
        harness.Context.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(),
            FarmId = harness.FarmId,
            Name = name
        });

        await harness.Context.SaveChangesAsync();
    }

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }
}
