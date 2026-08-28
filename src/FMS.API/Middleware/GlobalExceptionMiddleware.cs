using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

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

        var response = new ProblemDetails
        {
            Status = statusCode,
            Title = GetTitle(statusCode),
            Detail = _env.IsDevelopment() ? exception.Message : "An unexpected error occurred. Please try again later.",
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
        429 => "Too Many Requests",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };
}
