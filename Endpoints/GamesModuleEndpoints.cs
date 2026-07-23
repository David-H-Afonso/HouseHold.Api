using Household.Api.Application.Interfaces;
using Household.Api.DTOs;

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
                IGamesDatabaseClient client,
                CancellationToken ct
            ) =>
                Results.Ok(
                    await client.GetGamesAsync(search, statusId, page ?? 1, pageSize ?? 24, ct)
                )
        );

        group.MapGet("/summary", async (IGamesDatabaseClient client, CancellationToken ct) =>
            Results.Ok(await client.GetSummaryAsync(ct))
        );

        group.MapGet("/statuses", async (IGamesDatabaseClient client, CancellationToken ct) =>
            Results.Ok(await client.GetStatusesAsync(ct))
        );

        group.MapGet(
            "/steam/search",
            async (string q, IGamesDatabaseClient client, CancellationToken ct) =>
                Results.Ok(await client.SearchSteamAsync(q, ct))
        );

        group.MapPost(
            "/steam/add",
            async (AddSteamGameRequest request, IGamesDatabaseClient client, CancellationToken ct) =>
                Results.Ok(await client.AddSteamGameAsync(request, ct))
        );

        group.MapGet(
            "/{id:int}",
            async (int id, IGamesDatabaseClient client, CancellationToken ct) =>
            {
                var game = await client.GetGameAsync(id, ct);
                return game is null ? Results.NotFound() : Results.Ok(game);
            }
        );

        group.MapPatch(
            "/{id:int}/status",
            async (
                int id,
                UpdateGameStatusRequest request,
                IGamesDatabaseClient client,
                CancellationToken ct
            ) =>
            {
                var game = await client.UpdateStatusAsync(id, request.StatusId, ct);
                return game is null ? Results.NotFound() : Results.Ok(game);
            }
        );
    }
}
