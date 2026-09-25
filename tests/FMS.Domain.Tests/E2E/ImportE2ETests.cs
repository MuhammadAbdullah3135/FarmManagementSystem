using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FMS.Domain.Tests.E2E;

/// <summary>
/// The employee and inventory imports over HTTP, through the real controllers and the
/// real <c>FarmContextMiddleware</c> — the same farm-scoped route treatment every other
/// <c>/api/farm/{farmId}/…</c> endpoint gets, and one more check that a caller cannot
/// write into a farm they do not belong to.
///
/// The animal importer has had this coverage since 3.4; these two are what 4.3 added, and
/// they are deliberately checked at the HTTP layer rather than only against the services,
/// because the routes, the authorization attribute and the farm gate are the parts a unit
/// test cannot see.
/// </summary>
public class ImportE2ETests : IClassFixture<ImportE2ETests.Factory>
{
    private const string EmployeeHeaders =
        "firstName,lastName,email,department,role,salaryType,salaryRate";

    private const string InventoryHeaders = "name,unit,quantity,reorderLevel,unitCost,category";

    private const string ExpenseHeaders = "date,amount,category,payment,animal,location,description";

    private readonly Factory _factory;

    public ImportE2ETests(Factory factory) => _factory = factory;

    private Guid FarmId
    {
        get
        {
            _ = _factory.Host;
            return _factory.SeedFarmId;
        }
    }

    private HttpClient Client(Guid farmId) => _factory.CreateAuthenticatedClient(farmId);

    public class Factory : TestWebApplicationFactory
    {
        protected override bool UsesRealFarmContextMiddleware => true;

        /// <summary>A farm the seeded test user has no membership in.</summary>
        public Guid ForeignFarmId { get; private set; }

        public Guid SeedFarmId { get; private set; }

        protected override async Task SeedAsync(FmsDbContext db, ApiSeedData.SeedIds seed)
        {
            SeedFarmId = seed.FarmId;

            // The employee importer resolves these by name, and refuses a name the farm
            // does not have — so a farm with no department or role can import nobody.
            db.Departments.Add(new Department { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Dairy" });
            db.EmployeeRoles.Add(new EmployeeRole { Id = Guid.NewGuid(), FarmId = seed.FarmId, Name = "Milker" });

            // The finance importers resolve the farm's own categories, payment methods and
            // locations by name; the base seed already provides Feed, Cash, Milk Sales and
            // Main Barn, so the tests below import against those rather than adding more.

            ForeignFarmId = Guid.NewGuid();
            db.Farms.Add(new Farm { Id = ForeignFarmId, AccountId = seed.AccountId, Name = "Foreign Farm" });

            await db.SaveChangesAsync();
        }
    }

    // ── employees ───────────────────────────────────────────

    [Fact]
    public async Task EmployeePreview_ReportsTheFileAgainstTheFarmsOwnDepartments_AndCreatesNothing()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var email = Email("preview");

        using var response = await PostAsync(
            client, farmId, "employees", "preview", EmployeeFile(email, "Dairy", "Milker"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = await response.Content.ReadFromJsonAsync<PreviewShape>();
        Assert.NotNull(preview);
        Assert.Equal(1, preview!.TotalRows);
        Assert.Equal(1, preview.ValidRowCount);
        Assert.Equal(0, preview.InvalidRowCount);

        // The mapping step is built from the response, including the farm's own
        // department and role names.
        Assert.Equal(new[] { "Dairy" }, preview.Lookups["department"]);
        Assert.Equal(new[] { "Milker" }, preview.Lookups["role"]);

        // Nothing was written: the address the preview just accepted is not on any
        // employee in the farm.
        Assert.DoesNotContain(await ListEmployeesAsync(client, farmId), employee => employee.Email == email);
    }

    [Fact]
    public async Task EmployeeCommit_ImportsTheFile_AndTheEmployeesShowUpInTheListEndpoint()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var email = Email("commit");

        using var response = await PostAsync(
            client, farmId, "employees", "commit", EmployeeFile(email, "Dairy", "Milker"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var commit = await response.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(1, commit!.ImportedCount);
        Assert.Empty(commit.InvalidRows);

        var employee = Assert.Single(await ListEmployeesAsync(client, farmId), candidate => candidate.Email == email);
        Assert.Equal("Amina", employee.FirstName);
        Assert.Equal("Dairy", employee.DepartmentName);
        Assert.Equal("Milker", employee.EmployeeRoleName);
        Assert.Equal(45000m, employee.SalaryRate);
    }

    [Fact]
    public async Task EmployeeCommit_WithOneInvalidRow_ImportsNothing()
    {
        var farmId = FarmId;
        var client = Client(farmId);
        var before = (await ListEmployeesAsync(client, farmId)).Count;

        // The second row names a department this farm does not have.
        var csv = EmployeeHeaders + "\n" +
                  $"Amina,Yusuf,{Email("mixed")},Dairy,Milker,Monthly,45000\n" +
                  $"Bilal,Otieno,{Email("mixed2")},Poultry,Milker,Monthly,8000\n";

        using var response = await PostAsync(client, farmId, "employees", "commit", csv);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var commit = await response.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(2, commit!.TotalRows);
        Assert.Equal(0, commit.ImportedCount);

        var invalid = Assert.Single(commit.InvalidRows);
        Assert.Equal(3, invalid.RowNumber);
        Assert.Contains("Poultry", invalid.Errors[0].Message);

        // The valid row of the file was not written either.
        Assert.Equal(before, (await ListEmployeesAsync(client, farmId)).Count);
    }

    [Fact]
    public async Task EmployeeCommit_DuplicateEmail_IsRefusedRatherThanSkipped()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var email = Email("dupe");

        using var first = await PostAsync(
            client, farmId, "employees", "commit", EmployeeFile(email, "Dairy", "Milker"));
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<CommitShape>())!.ImportedCount);

        // The same address again, in a later file — upper-cased, because an address is
        // the same person however it is typed.
        using var second = await PostAsync(
            client, farmId, "employees", "commit", EmployeeFile(email.ToUpperInvariant(), "Dairy", "Milker"));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var commit = await second.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(0, commit!.ImportedCount);
        Assert.Equal("An employee with this email already exists", Assert.Single(Assert.Single(commit.InvalidRows).Errors).Message);

        // Exactly one employee holds the address: the duplicate was refused, not merged
        // into it and not skipped silently.
        Assert.Single(await ListEmployeesAsync(client, farmId), candidate =>
            string.Equals(candidate.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    // ── inventory ───────────────────────────────────────────

    [Fact]
    public async Task InventoryCommit_ImportsTheFile_AndTheItemsShowUpInTheListEndpoint()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        // Opening stock is non-zero on purpose: a zero quantity records no movement, so
        // a zero here would assert nothing about the ledger entry below.
        var name = Unique("Vet wrap");
        var csv = $"{InventoryHeaders}\n{name},roll,25,5,3.25,Medical\n";

        using var response = await PostAsync(client, farmId, "inventory-items", "commit", csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var commit = await response.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(1, commit!.ImportedCount);

        var items = await ListInventoryAsync(client, farmId);
        var item = Assert.Single(items, candidate => candidate.Name == name);
        Assert.Equal("roll", item.Unit);
        Assert.Equal(25m, item.Quantity);
        Assert.Equal(5m, item.ReorderLevel);
        Assert.Equal(3.25m, item.UnitCost);

        // Opening stock is recorded as a movement, exactly as the create endpoint does.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FmsDbContext>();
        var movement = Assert.Single(await db.StockMovements
            .Where(movement => movement.InventoryItemId == item.Id)
            .ToListAsync());
        Assert.Equal("Opening stock", movement.Reason);
    }

    [Fact]
    public async Task InventoryCommit_DuplicateItemName_IsRefusedRatherThanOverwriting()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var csv = $"{InventoryHeaders}\nHay Bales,bales,100,10,15,\n";

        // "Hay Bales" is in the seed data: the item exists, so the row is an error.
        using var response = await PostAsync(client, farmId, "inventory-items", "commit", csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var commit = await response.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(0, commit!.ImportedCount);
        Assert.Equal("An inventory item with this name already exists",
            Assert.Single(Assert.Single(commit.InvalidRows).Errors).Message);

        // Still the seeded item, with its own quantity — not the file's.
        var item = Assert.Single(await ListInventoryAsync(client, farmId), candidate => candidate.Name == "Hay Bales");
        Assert.Equal(5m, item.Quantity);
    }

    // ── suppliers and customers ─────────────────────────────

    [Fact]
    public async Task SupplierCommit_ImportsTheFile_AndTheSuppliersShowUpInTheListEndpoint()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var name = Unique("Supplier");
        var csv = $"supplier,contact,products\n{name},+254 700 000000,Feed\n";

        using var response = await PostAsync(client, farmId, "inventory/suppliers", "commit", csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var commit = await response.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(1, commit!.ImportedCount);

        var supplier = Assert.Single(await ListSuppliersAsync(client, farmId), candidate => candidate.Name == name);
        Assert.Equal("+254 700 000000", supplier.ContactInfo);
        Assert.Equal("Feed", supplier.ProductsSupplied);
    }

    [Fact]
    public async Task SupplierCommit_DuplicateName_IsRefusedRatherThanSkipped()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var csv = $"supplier,contact,products\n{Unique("Supplier")},,Feed\n";

        using var first = await PostAsync(client, farmId, "inventory/suppliers", "commit", csv);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<CommitShape>())!.ImportedCount);

        using var second = await PostAsync(client, farmId, "inventory/suppliers", "commit", csv);
        var commit = await second.Content.ReadFromJsonAsync<CommitShape>();
        Assert.Equal(0, commit!.ImportedCount);
        Assert.Equal("A supplier with this name already exists",
            Assert.Single(Assert.Single(commit.InvalidRows).Errors).Message);
    }

    [Fact]
    public async Task CustomerCommit_ImportsTheFile_AndTheCustomersShowUpInTheListEndpoint()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var name = Unique("Customer");
        using var response = await PostAsync(
            client, farmId, "inventory/customers", "commit", $"customer,contact\n{name},+254 711 111111\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<CommitShape>())!.ImportedCount);

        var customer = Assert.Single(await ListCustomersAsync(client, farmId), candidate => candidate.Name == name);
        Assert.Equal("+254 711 111111", customer.ContactInfo);
    }

    // ── expenses and income ─────────────────────────────────

    [Fact]
    public async Task ExpenseCommit_ImportsTheFile_AndTheExpensesShowUpInTheListEndpoint()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var description = Unique("Expense");
        var csv = $"{ExpenseHeaders}\n2026-01-15,1500.50,Feed,Cash,,Main Barn,{description}\n";

        using var response = await PostAsync(client, farmId, "finance/expenses", "commit", csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<CommitShape>())!.ImportedCount);

        var expense = Assert.Single(await ListExpensesAsync(client, farmId), candidate => candidate.Description == description);
        Assert.Equal(1500.50m, expense.Amount);
        Assert.Equal("Feed", expense.ExpenseCategoryName);
        Assert.Equal("Cash", expense.PaymentMethodName);
    }

    [Fact]
    public async Task ExpenseCommit_IdenticalRows_AreBothWritten_BecauseAnExpenseHasNoDuplicate()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        // The deliberate no-duplicate policy, over HTTP: an expense has no identifier, so
        // two identical rows are two real transactions and both are imported.
        var description = Unique("Repeated expense");
        var row = $"2026-01-15,321.00,Feed,Cash,,,{description}";
        var csv = $"{ExpenseHeaders}\n{row}\n{row}\n";

        using var response = await PostAsync(client, farmId, "finance/expenses", "commit", csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<CommitShape>())!.ImportedCount);

        Assert.Equal(2, (await ListExpensesAsync(client, farmId)).Count(candidate => candidate.Description == description));
    }

    [Fact]
    public async Task IncomeCommit_ImportsTheFile_AndTheRecordsShowUpInTheListEndpoint()
    {
        var farmId = FarmId;
        var client = Client(farmId);

        var description = Unique("Income");
        var csv = $"{ExpenseHeaders}\n2026-01-20,2500,Milk Sales,Cash,,,{description}\n";

        using var response = await PostAsync(client, farmId, "finance/income-records", "commit", csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<CommitShape>())!.ImportedCount);

        var record = Assert.Single(await ListIncomeAsync(client, farmId), candidate => candidate.Description == description);
        Assert.Equal(2500m, record.Amount);
        Assert.Equal("Milk Sales", record.IncomeCategoryName);
    }

    // ── the farm gate, on every route ───────────────────────

    [Theory]
    [InlineData("employees")]
    [InlineData("inventory-items")]
    [InlineData("inventory/suppliers")]
    [InlineData("inventory/customers")]
    [InlineData("finance/expenses")]
    [InlineData("finance/income-records")]
    public async Task Import_IntoAFarmTheCallerIsNotAMemberOf_IsForbiddenAndWritesNothing(string entity)
    {
        _ = _factory.Host;
        var farmId = _factory.ForeignFarmId;
        var client = Client(farmId);
        var csv = CsvFor(entity);

        using var preview = await PostAsync(client, farmId, entity, "preview", csv);
        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);

        using var commit = await PostAsync(client, farmId, entity, "commit", csv);
        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);
    }

    [Theory]
    [InlineData("employees")]
    [InlineData("inventory-items")]
    [InlineData("inventory/suppliers")]
    [InlineData("inventory/customers")]
    [InlineData("finance/expenses")]
    [InlineData("finance/income-records")]
    public async Task Import_WithNoToken_IsUnauthorized(string entity)
    {
        var farmId = FarmId;
        using var client = _factory.CreateClient();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes($"{InventoryHeaders}\nX,kg,1,1,1,\n")), "file", "file.csv");

        using var response = await client.PostAsync($"/api/farm/{farmId}/{entity}/import/preview", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── helpers ─────────────────────────────────────────────

    /// <summary>
    /// The tests share one host and therefore one database, so every fixture value is
    /// unique per test: a fixed address would make the first test to run decide whether
    /// the others see a duplicate.
    /// </summary>
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}";

    private static string Email(string prefix) => $"{Unique(prefix)}@example.com";

    private static string EmployeeFile(string email, string department, string role) =>
        EmployeeHeaders + "\n" + $"Amina,Yusuf,{email},{department},{role},Monthly,45000\n";

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, Guid farmId, string entity, string action, string csv)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", $"{entity}.csv");

        return await client.PostAsync($"/api/farm/{farmId}/{entity}/import/{action}", content);
    }

    private static async Task<List<EmployeeShape>> ListEmployeesAsync(HttpClient client, Guid farmId)
    {
        var page = await client.GetFromJsonAsync<EmployeePageShape>($"/api/farm/{farmId}/employees?pageSize=50");
        return page?.Items ?? new List<EmployeeShape>();
    }

    private static async Task<List<InventoryShape>> ListInventoryAsync(HttpClient client, Guid farmId)
    {
        var page = await client.GetFromJsonAsync<InventoryPageShape>($"/api/farm/{farmId}/inventory-items?pageSize=50");
        return page?.Items ?? new List<InventoryShape>();
    }

    /// <summary>
    /// A well-formed file for each import route. For the 401/403 theories the body is
    /// never parsed — the request is refused before the pipeline — so the values only
    /// need to be non-empty and shaped like the entity.
    /// </summary>
    private static string CsvFor(string entity) => entity switch
    {
        "employees" => EmployeeFile(Email("foreign"), "Dairy", "Milker"),
        "inventory-items" => $"{InventoryHeaders}\n{Unique("Item")},kg,1,1,1,\n",
        "inventory/suppliers" => "supplier,contact,products\nKilimo Feeds,,Feed\n",
        "inventory/customers" => "customer,contact\nNairobi Dairy Co-op,\n",
        "finance/expenses" => $"{ExpenseHeaders}\n2026-01-15,100,Feed,Cash,,Main Barn,Dairy meal\n",
        _ => $"{ExpenseHeaders}\n2026-01-20,100,Milk Sales,Cash,,,Morning milk\n",
    };

    private static async Task<List<SupplierShape>> ListSuppliersAsync(HttpClient client, Guid farmId)
    {
        var page = await client.GetFromJsonAsync<SupplierPageShape>($"/api/farm/{farmId}/inventory/suppliers?pageSize=50");
        return page?.Items ?? new List<SupplierShape>();
    }

    private static async Task<List<CustomerShape>> ListCustomersAsync(HttpClient client, Guid farmId)
    {
        var page = await client.GetFromJsonAsync<CustomerPageShape>($"/api/farm/{farmId}/inventory/customers?pageSize=50");
        return page?.Items ?? new List<CustomerShape>();
    }

    private static async Task<List<ExpenseShape>> ListExpensesAsync(HttpClient client, Guid farmId)
    {
        var page = await client.GetFromJsonAsync<ExpensePageShape>($"/api/farm/{farmId}/finance/expenses?pageSize=50");
        return page?.Items ?? new List<ExpenseShape>();
    }

    private static async Task<List<IncomeShape>> ListIncomeAsync(HttpClient client, Guid farmId)
    {
        var page = await client.GetFromJsonAsync<IncomePageShape>($"/api/farm/{farmId}/finance/income-records?pageSize=50");
        return page?.Items ?? new List<IncomeShape>();
    }

    private sealed record PreviewShape(
        int TotalRows,
        int ValidRowCount,
        int InvalidRowCount,
        Dictionary<string, List<string>> Lookups,
        List<RowShape> InvalidRows,
        List<RowShape> SampleValidRows);

    private sealed record CommitShape(int TotalRows, int ImportedCount, List<RowShape> InvalidRows);

    private sealed record RowShape(int RowNumber, Dictionary<string, string> Values, List<ErrorShape> Errors);

    private sealed record ErrorShape(string Field, string Message);

    private sealed record EmployeePageShape(List<EmployeeShape> Items, int TotalCount);

    private sealed record EmployeeShape(
        Guid Id, string FirstName, string LastName, string? Email,
        string? DepartmentName, string? EmployeeRoleName, decimal SalaryRate);

    private sealed record InventoryPageShape(List<InventoryShape> Items, int TotalCount);

    private sealed record InventoryShape(Guid Id, string Name, string Unit, decimal Quantity, decimal ReorderLevel, decimal UnitCost);

    private sealed record SupplierPageShape(List<SupplierShape> Items, int TotalCount);

    private sealed record SupplierShape(Guid Id, string Name, string? ContactInfo, string? ProductsSupplied);

    private sealed record CustomerPageShape(List<CustomerShape> Items, int TotalCount);

    private sealed record CustomerShape(Guid Id, string Name, string? ContactInfo);

    private sealed record ExpensePageShape(List<ExpenseShape> Items, int TotalCount);

    private sealed record ExpenseShape(
        Guid Id, decimal Amount, string ExpenseCategoryName, string PaymentMethodName, string? Description);

    private sealed record IncomePageShape(List<IncomeShape> Items, int TotalCount);

    private sealed record IncomeShape(
        Guid Id, decimal Amount, string IncomeCategoryName, string PaymentMethodName, string? Description);
}
