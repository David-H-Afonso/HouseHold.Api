using System.Security.Claims;
using Household.Api.Models.Auth;

namespace Household.Api.Middleware;

public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    public const string ErrorCode = "password_change_required";

    private static readonly HashSet<string> AllowedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/auth/me",
        "/auth/change-password",
        "/auth/logout",
        "/auth/logout-all",
        "/health",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var requiresPasswordChange =
            context.User.Identity?.IsAuthenticated == true
            && string.Equals(
                context.User.FindFirstValue(HouseholdClaimTypes.RequiresPasswordChange),
                bool.TrueString,
                StringComparison.OrdinalIgnoreCase
            );

        if (requiresPasswordChange && !AllowedPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(
                new { code = ErrorCode },
                cancellationToken: context.RequestAborted
            );
            return;
        }

        await next(context);
    }
}
