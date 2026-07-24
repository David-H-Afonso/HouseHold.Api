using System.Net;
using System.Text.Json;
using Household.Api.Application.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        (int statusCode, string message, string? details, string code, bool reconcilable) = exception switch
        {
            IntegrationGatewayException gatewayException => (
                (int)gatewayException.StatusCode,
                gatewayException.Message,
                (string?)null,
                gatewayException.Code,
                gatewayException.Reconcilable
            ),
            DbUpdateException { InnerException: SqliteException sqEx } => sqEx.SqliteErrorCode == 19
                ? ((int)HttpStatusCode.Conflict, "Conflict: duplicate or constraint violation", null, "conflict", false)
                : ((int)HttpStatusCode.BadRequest, "Database error", null, "database_error", false),

            DbUpdateException => ((int)HttpStatusCode.BadRequest, "Error saving data", null, "database_error", false),

            ArgumentException => ((int)HttpStatusCode.BadRequest, "Invalid data", null, "invalid_request", false),

            UnauthorizedAccessException => ((int)HttpStatusCode.Forbidden, "Access denied", null, "forbidden", false),

            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "Resource not found", null, "not_found", false),

            _ => ((int)HttpStatusCode.InternalServerError, "An unexpected error occurred", null, "unexpected_error", false),
        };

        context.Response.StatusCode = statusCode;
        var payload = JsonSerializer.Serialize(
            new
            {
                statusCode,
                message,
                details,
                code,
                reconcilable,
            },
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            }
        );

        await context.Response.WriteAsync(payload);
    }
}
