using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;
using System.Security.Cryptography;

namespace Household.Api.Endpoints;

public static class GamesModuleEndpoints
{
    public static void MapGamesModuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/modules/games").WithTags("Games").RequireAuthorization();

        group.MapGet(
            "/",
            async (
                string? search,
                int? statusId,
                int? page,
                int? pageSize,
                HttpContext context,
                IGamesDatabaseClient client,
                CancellationToken ct
            ) =>
            {
                var userId = context.GetUserId();
                return userId is null
                    ? Results.Unauthorized()
                    : Results.Ok(await client.GetGamesAsync(userId.Value, search, statusId, page ?? 1, pageSize ?? 24, ct));
            }
        );

        group.MapGet("/summary", async (HttpContext context, IGamesDatabaseClient client, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await client.GetSummaryAsync(userId.Value, ct));
        });

        group.MapGet("/statuses", async (HttpContext context, IGamesDatabaseClient client, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await client.GetStatusesAsync(userId.Value, ct));
        });

        group.MapGet(
            "/{id:int}",
            async (int id, HttpContext context, IGamesDatabaseClient client, CancellationToken ct) =>
            {
                var userId = context.GetUserId();
                if (userId is null)
                    return Results.Unauthorized();
                var game = await client.GetGameAsync(userId.Value, id, ct);
                return game is null ? Results.NotFound() : Results.Ok(game);
            }
        );

        group.MapGet("/assets/{id:int}/{kind}", async (
            int id,
            string kind,
            HttpContext context,
            IGamesDatabaseClient client,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            var asset = await client.GetAssetAsync(userId.Value, id, kind, ct);
            if (asset is null) return Results.NotFound();

            var etag = $"\"{Convert.ToHexString(SHA256.HashData(asset.Value.Content))}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "private, max-age=3600, must-revalidate";
            if (context.Request.Headers.IfNoneMatch.Any(value => value == etag))
                return Results.StatusCode(StatusCodes.Status304NotModified);

            return Results.File(asset.Value.Content, asset.Value.ContentType, enableRangeProcessing: false);
        }).RequireRateLimiting("asset");

        group.MapPatch(
            "/{id:int}/status",
            async (
                int id,
                UpdateGameStatusRequest request,
                HttpContext context,
                IGamesDatabaseClient client,
                CancellationToken ct
            ) =>
            {
                var userId = context.GetUserId();
                if (userId is null)
                    return Results.Unauthorized();
                var game = await client.UpdateStatusAsync(userId.Value, id, request.StatusId, ct);
                return game is null ? Results.NotFound() : Results.Ok(game);
            }
        ).RequireRateLimiting("mutation");
    }
}
