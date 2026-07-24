using Household.Api.Application.Interfaces;
using Household.Api.Helpers;
using Household.Api.DTOs;

namespace Household.Api.Endpoints;

public static class WarcraftModuleEndpoints
{
    public static void MapWarcraftModuleEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/modules/warcraft/quick-status",
                async (HttpContext context, IWarcraftArchiveClient client, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    return userId is null
                        ? Results.Unauthorized()
                        : Results.Ok(await client.GetQuickStatusAsync(userId.Value, ct));
                }
            )
            .WithTags("Warcraft")
            .RequireAuthorization();

        app.MapPatch("/modules/warcraft/trackings/{id:guid}/status", async (
            Guid id,
            UpdateWarcraftTrackingStatusRequest request,
            HttpContext context,
            IWarcraftArchiveClient client,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await client.UpdateTrackingStatusAsync(userId.Value, id, request.Status, ct));
        }).WithTags("Warcraft").RequireAuthorization().RequireRateLimiting("mutation");

        app.MapGet(
                "/modules/warcraft/weekly",
                async (HttpContext context, IWarcraftArchiveClient client, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    return userId is null
                        ? Results.Unauthorized()
                        : Results.Ok(await client.GetWeeklyAsync(userId.Value, ct));
                }
            )
            .WithTags("Warcraft")
            .RequireAuthorization();
    }
}
