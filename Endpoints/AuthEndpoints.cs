using System.Security.Claims;
using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Services;

namespace Household.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        group
            .MapPost(
                "/login",
                async (LoginRequest request, IAuthService authService, HttpContext ctx) =>
                {
                    var userAgent = ctx.Request.Headers.UserAgent.ToString();
                    var deviceName = ctx.Request.Headers["X-Device-Name"].ToString();

                    var result = await authService.LoginAsync(request.Email, request.Password, userAgent, deviceName);
                    return result == null ? Results.Unauthorized() : Results.Ok(result);
                }
            )
            .WithName("Login")
            .WithSummary("Authenticate and get access + refresh tokens")
            .RequireRateLimiting("login");

        group
            .MapPost(
                "/refresh",
                async (RefreshRequest request, IAuthService authService, HttpContext ctx) =>
                {
                    var userAgent = ctx.Request.Headers.UserAgent.ToString();

                    var result = await authService.RefreshAsync(request.RefreshToken, userAgent, request.DeviceName);
                    return result == null ? Results.Unauthorized() : Results.Ok(result);
                }
            )
            .WithName("Refresh")
            .WithSummary("Rotate refresh token and get a new access token")
            .RequireRateLimiting("login");

        group
            .MapPost(
                "/logout",
                async (LogoutRequest request, IAuthService authService) =>
                {
                    await authService.LogoutAsync(request.RefreshToken);
                    return Results.NoContent();
                }
            )
            .WithName("Logout")
            .WithSummary("Revoke a refresh token");

        // ── Authenticated endpoints ───────────────────────────────────────────────────
        var authed = app.MapGroup("/auth").WithTags("Auth").RequireAuthorization();

        authed
            .MapGet(
                "/me",
                (HttpContext ctx) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    var email = ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
                    var userName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
                    var isAdmin = ctx.IsAdmin();
                    var requiresPasswordChange = string.Equals(
                        ctx.User.FindFirstValue(Household.Api.Models.Auth.HouseholdClaimTypes.RequiresPasswordChange),
                        bool.TrueString,
                        StringComparison.OrdinalIgnoreCase
                    );

                    return Results.Ok(new MeResponse(userId.Value, email, userName, isAdmin, requiresPasswordChange));
                }
            )
            .WithName("Me")
            .WithSummary("Get current authenticated user info (reads from token — no DB roundtrip)");

        authed
            .MapPost(
                "/change-password",
                async (
                    ChangePasswordRequest request,
                    HttpContext ctx,
                    IAuthService authService,
                    CancellationToken cancellationToken
                ) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    var error = await authService.ChangePasswordAsync(
                        userId.Value,
                        request.CurrentPassword,
                        request.NewPassword,
                        cancellationToken
                    );
                    return error == null
                        ? Results.Ok(new ChangePasswordResponse("password_changed", ReauthenticationRequired: true))
                        : Results.BadRequest(new { code = error });
                }
            )
            .WithName("ChangePassword")
            .WithSummary("Change the current user's password and revoke all sessions")
            .RequireRateLimiting("mutation");

        authed
            .MapPost(
                "/logout-all",
                async (HttpContext ctx, IAuthService authService) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    var count = await authService.LogoutAllAsync(userId.Value);
                    return Results.Ok(new { message = $"Revoked {count} active session(s)." });
                }
            )
            .WithName("LogoutAll")
            .WithSummary("Revoke all refresh tokens for the current user (logout from all devices)");
    }
}
