using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FMS.Infrastructure.Persistence;

/// <summary>
/// Lets the EF Core command-line tools build the model without launching the API.
///
/// Without a design-time factory the tools execute <c>Program</c>, which runs
/// <c>Database.MigrateAsync()</c> and role seeding as a side effect of asking for
/// a migration. The connection string here is never used to connect when adding
/// a migration — only the provider is needed to shape the model.
/// </summary>
public class FmsDbContextFactory : IDesignTimeDbContextFactory<FmsDbContext>
{
    public FmsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseNpgsql("Host=localhost;Database=fms_designtime;Username=postgres;Password=postgres")
            .Options;

        return new FmsDbContext(options);
    }
}
