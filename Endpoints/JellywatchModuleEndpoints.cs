using Household.Api.Application.Interfaces;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class JellywatchModuleEndpoints
{
    public static void MapJellywatchModuleEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/modules/media/jellywatch",
                async (HttpContext context, IJellywatchClient client, IUserSettingsService settings, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    if (userId is null) return Results.Unauthorized();
                    var preferences = await settings.GetPreferencesAsync(userId.Value, ct);
                    return Results.Ok(await client.GetDashboardAsync(userId.Value, preferences.TimeZoneId ?? "UTC", ct));
                }
            )
            .WithTags("Media")
            .RequireAuthorization();

        app.MapGet("/modules/media/jellywatch/posters/{mediaItemId:long}", async (
            long mediaItemId,
            string? source,
            HttpContext context,
            IJellywatchClient client,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            if (source is not ("upcoming" or "activity")) return Results.NotFound();
            var scope = source == "upcoming" ? "upcoming.read" : "activity.read";
            var poster = await client.GetPosterAsync(userId.Value, mediaItemId, scope, ct);
            context.Response.Headers.CacheControl = "private, no-store";
            return poster is null
                ? Results.NotFound()
                : Results.File(poster.Value.Content, poster.Value.ContentType, enableRangeProcessing: false);
        }).WithTags("Media").RequireAuthorization().RequireRateLimiting("asset");
    }
}
