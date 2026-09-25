using FMS.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// Counts the SQL round trips a report issues, against a real (in-process) database.
///
/// <para>
/// The suite's usual provider cannot do this at all. EF InMemory evaluates LINQ in memory and
/// issues no commands, so it has no signal to count — which is why the per-schedule, per-animal
/// loops these tests guard survived a green suite for so long. SQLite is relational, costs no
/// server, and runs anywhere CI does, so a round trip becomes an assertable number instead of a
/// stopwatch reading that depends on the machine.
/// </para>
///
/// <para>
/// Scope: the four methods rewritten in Phase 6.3. Those are safe here because their arithmetic
/// is already done in memory; SQLite cannot aggregate <c>decimal</c> in SQL, so a method that asks
/// the database to <c>SUM</c> a money column (the health-cost service, which has no such loop)
/// deliberately stays on the InMemory provider. The same counts are asserted against a real
/// PostgreSQL server in <see cref="ReportQueryCountPostgresTests"/>.
/// </para>
/// </summary>
public sealed class SqliteQueryCounter : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteQueryCounter()
    {
        // Kept open for the harness's lifetime: a SQLite in-memory database exists only while a
        // connection to it does.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Measured = new CountingCommandInterceptor();

        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(Measured)
            .Options;

        Context = new FmsDbContext(options);
        Context.Database.EnsureCreated();
    }

    public FmsDbContext Context { get; }

    public CountingCommandInterceptor Measured { get; }

    public int Readers => Measured.Readers;

    public int Writers => Measured.Writers;

    /// <summary>
    /// Runs <paramref name="action"/> with the counters zeroed and returns how many read round
    /// trips it cost. That number is the point: it must not move when the farm gets bigger.
    /// </summary>
    public async Task<int> CountReadersAsync(Func<Task> action)
    {
        Measured.Reset();
        await action();
        return Measured.Readers;
    }

    /// <summary>
    /// The same for a method that writes while it reads (the weight-check status also materialises
    /// tasks), so reads and writes are reported separately.
    /// </summary>
    public async Task<(int Readers, int Writers)> CountAllAsync(Func<Task> action)
    {
        Measured.Reset();
        await action();
        return (Measured.Readers, Measured.Writers);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
