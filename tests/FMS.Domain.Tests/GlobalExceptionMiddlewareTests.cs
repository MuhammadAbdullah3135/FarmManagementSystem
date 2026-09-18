using FMS.API.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace FMS.Domain.Tests;

/// <summary>
/// The safety net behind the per-lookup guards: a write that clashes with existing data must be a
/// 409 the client can act on, never the generic "Internal Server Error" that a bare foreign-key
/// violation used to produce.
/// </summary>
public class GlobalExceptionMiddlewareTests
{
    private static async Task<(int Status, string Body)> InvokeAsync(Exception exception, bool isDevelopment = false)
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw exception,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            new FakeEnvironment(isDevelopment));

        var context = new DefaultHttpContext();
        context.Request.Method = "DELETE";
        context.Request.Path = "/api/farm/11111111-1111-1111-1111-111111111111/configuration/location-types/22222222-2222-2222-2222-222222222222";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private static PostgresException ForeignKeyViolation() =>
        new("update or delete on table \"LocationTypes\" violates foreign key constraint",
            "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);

    [Fact]
    public async Task ForeignKeyViolation_IsReportedAs409WithAnActionableReason()
    {
        var (status, body) = await InvokeAsync(
            new DbUpdateException("delete failed", ForeignKeyViolation()));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("still referenced by other data and cannot be deleted", body);
        // The database's own wording must never reach the client.
        Assert.DoesNotContain("foreign key constraint", body);
    }

    [Fact]
    public async Task BarePostgresForeignKeyViolation_IsReportedAs409()
    {
        var (status, body) = await InvokeAsync(ForeignKeyViolation());

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("still referenced by other data and cannot be deleted", body);
    }

    [Fact]
    public async Task ConcurrencyClash_IsReportedAs409()
    {
        var (status, body) = await InvokeAsync(new DbUpdateConcurrencyException("row changed"));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("changed by another user", body);
    }

    [Fact]
    public async Task OtherPostgresFailures_Remain500AndHideTheDetailInProduction()
    {
        // 23505 = unique_violation, a different class of failure.
        var (status, body) = await InvokeAsync(
            new DbUpdateException("insert failed",
                new PostgresException("duplicate key value", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Contains("An unexpected error occurred", body);
        Assert.DoesNotContain("duplicate key value", body);
    }

    [Fact]
    public async Task UnhandledFailure_ShowsTheReasonInDevelopmentOnly()
    {
        var (status, body) = await InvokeAsync(new InvalidOperationException("boom"), isDevelopment: true);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Contains("boom", body);
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(bool isDevelopment) =>
            EnvironmentName = isDevelopment ? Environments.Development : Environments.Production;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "FMS.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
