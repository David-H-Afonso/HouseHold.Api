using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class JellyfinEndpoints
{
    public static void MapJellyfinEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/jellyfin").WithTags("Jellyfin").RequireAuthorization();
        group.MapGet("/config", async (HttpContext context, IJellyfinService service, CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.GetConfigAsync(ct)) : Results.Forbid());
        group.MapPut("/config", async (
            UpdateJellyfinConfigRequest request,
            HttpContext context,
            IJellyfinService service,
            CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.UpdateConfigAsync(request, ct)) : Results.Forbid()
        ).RequireRateLimiting("admin");
        group.MapGet("/dashboard", async (HttpContext context, IJellyfinService service, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.GetDashboardAsync(userId.Value, ct));
        });
        group.MapGet("/images/{itemId}", async (
            string itemId,
            HttpContext context,
            IJellyfinService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null || string.IsNullOrWhiteSpace(itemId) || itemId.Length > 128) return Results.NotFound();
            var image = await service.GetImageAsync(userId.Value, itemId, ct);
            context.Response.Headers.CacheControl = "private, no-store";
            return image is null
                ? Results.NotFound()
                : Results.File(image.Value.Content, image.Value.ContentType, enableRangeProcessing: false);
        }).RequireRateLimiting("asset");
    }
}
