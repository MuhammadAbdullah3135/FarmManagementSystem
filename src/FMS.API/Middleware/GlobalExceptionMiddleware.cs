using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FMS.API.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var farmId = context.Request.Headers["X-Farm-Id"].FirstOrDefault();
        var method = context.Request.Method;
        var path = context.Request.Path;
        var ipAddress = context.Connection.RemoteIpAddress?.ToString();

        // A write that clashes with existing data is the caller's problem to fix, not a server
        // fault: answer 409 with something actionable instead of an opaque 500.
        var conflictDetail = ClassifyConflict(exception);
        if (conflictDetail is not null)
        {
            _logger.LogWarning(exception,
                "Rejected write | TraceId={TraceId} UserId={UserId} FarmId={FarmId} Method={Method} Path={Path} Ip={Ip}",
                traceId, userId ?? "anonymous", farmId ?? "none", method, path, ipAddress ?? "unknown");

            await WriteProblemAsync(context, (int)HttpStatusCode.Conflict, path, traceId, conflictDetail);
            return;
        }

        // Structured logging with context
        _logger.LogError(exception,
            "Unhandled exception | TraceId={TraceId} UserId={UserId} FarmId={FarmId} Method={Method} Path={Path} Ip={Ip}",
            traceId, userId ?? "anonymous", farmId ?? "none", method, path, ipAddress ?? "unknown");

        var statusCode = exception switch
        {
            UnauthorizedAccessException => (int)HttpStatusCode.Forbidden,
            ArgumentException => (int)HttpStatusCode.BadRequest,
            KeyNotFoundException => (int)HttpStatusCode.NotFound,
            OperationCanceledException => (int)HttpStatusCode.ServiceUnavailable,
            _ => (int)HttpStatusCode.InternalServerError
        };

        await WriteProblemAsync(context, statusCode, path, traceId,
            _env.IsDevelopment() ? exception.Message : "An unexpected error occurred. Please try again later.");
    }

    /// <summary>
    /// Classifies a failed write that cannot succeed until the data is changed.
    ///
    /// Foreign keys are Restrict throughout the schema, so deleting or re-keying a row that other
    /// rows still reference fails inside the database (Postgres 23503). Without this the client
    /// only sees a generic internal server error and has no idea what is in the way.
    /// </summary>
    private static string? ClassifyConflict(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException
            => "This record was changed by another user. Reload and try again.",
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } }
            => "This record is still referenced by other data and cannot be deleted.",
        // Raw-SQL paths surface the violation without the DbUpdateException wrapper.
        PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation }
            => "This record is still referenced by other data and cannot be deleted.",
        _ => null
    };

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, PathString path, string traceId, string detail)
    {
        var response = new ProblemDetails
        {
            Status = statusCode,
            Title = GetTitle(statusCode),
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = path
        };

        response.Extensions["traceId"] = traceId;

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        429 => "Too Many Requests",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };
}
