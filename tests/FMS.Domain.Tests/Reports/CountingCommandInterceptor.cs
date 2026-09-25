using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FMS.Domain.Tests.Reports;

/// <summary>
/// Counts the commands a context executes, splitting them into reads and writes.
///
/// <para>
/// Commands are classified by their text rather than by which interceptor hook fired. That is not
/// fussiness: on SQLite the modification batch is executed through the <em>reader</em> path, so
/// counting per hook reports a farm's task inserts as extra "queries" and reports zero writes —
/// exactly the kind of miscount that would make a round-trip assertion meaningless.
/// </para>
/// </summary>
public sealed class CountingCommandInterceptor : DbCommandInterceptor
{
    private int _readers;
    private int _writers;

    /// <summary>Read round trips — a <c>SELECT</c>, whatever hook EF happened to use.</summary>
    public int Readers => _readers;

    /// <summary>Write round trips — <c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>/<c>MERGE</c>.</summary>
    public int Writers => _writers;

    public void Reset()
    {
        _readers = 0;
        _writers = 0;
    }

    private void Count(DbCommand command)
    {
        if (IsWrite(command.CommandText))
            _writers++;
        else
            _readers++;
    }

    private static bool IsWrite(string commandText)
    {
        var text = commandText.AsSpan().TrimStart();

        return text.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Count(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Count(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Count(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Count(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Count(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Count(command);
        return ValueTask.FromResult(result);
    }
}
