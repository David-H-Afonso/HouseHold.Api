using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;
using Household.Api.Application.Services;
using Household.Api.Application.Exceptions;

namespace Household.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var preferences = app.MapGroup("/api/v1/preferences").WithTags("Settings").RequireAuthorization();
        preferences.MapGet("/", async (HttpContext context, IUserSettingsService service, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.GetPreferencesAsync(userId.Value, ct));
        });
        preferences.MapPatch("/", async (
            UpdateUserPreferencesRequest request,
            HttpContext context,
            IUserSettingsService service,
            IJellyfinService jellyfin,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            if (request.JellyfinUserId is not null && !request.ClearJellyfinUserId)
            {
                try
                {
                    if (!await jellyfin.ValidateUserAsync(request.JellyfinUserId, ct))
                        return Results.BadRequest(new { code = "jellyfin_user_not_found" });
                }
                catch (IntegrationGatewayException)
                {
                    return Results.BadRequest(new { code = "jellyfin_unavailable" });
                }
            }
            return Results.Ok(await service.UpdatePreferencesAsync(userId.Value, request, ct));
        }).RequireRateLimiting("mutation");

        var dashboard = app.MapGroup("/api/v1/dashboard").WithTags("Dashboard Settings").RequireAuthorization();
        dashboard.MapGet("/catalog", (IUserSettingsService service) => Results.Ok(new
        {
            schemaVersion = UserSettingsService.CurrentSchemaVersion,
            widgets = service.GetWidgetCatalog(),
        }));
        dashboard.MapGet("/layout", async (HttpContext context, IUserSettingsService service, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.GetLayoutAsync(userId.Value, ct));
        });
        dashboard.MapPatch("/layout", async (
            UpdateDashboardLayoutRequest request,
            HttpContext context,
            IUserSettingsService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await service.UpdateLayoutAsync(userId.Value, request, ct));
        }).RequireRateLimiting("mutation");
        dashboard.MapPost("/layout/reset", async (HttpContext context, IUserSettingsService service, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.ResetLayoutAsync(userId.Value, ct));
        }).RequireRateLimiting("mutation");
    }
}
