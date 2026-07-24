using Household.Api.Application.Interfaces;
using Household.Api.Helpers;
using Household.Api.DTOs;

namespace Household.Api.Endpoints;

public static class PokemonModuleEndpoints
{
    public static void MapPokemonModuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/modules/pokemon").WithTags("Pokemon").RequireAuthorization();

        group.MapGet(
            "/",
            async (
                string? search,
                string? tagIds,
                int? skip,
                int? take,
                HttpContext context,
                IBeastVaultClient client,
                IUserSettingsService settings,
                CancellationToken ct
            ) =>
            {
                var userId = context.GetUserId();
                if (userId is null)
                    return Results.Unauthorized();
                if (!TryParseTagIds(tagIds, out var parsedTagIds))
                    return Results.BadRequest(new { message = "tag_ids_must_be_positive_integers" });

                var preferences = await settings.GetPreferencesAsync(userId.Value, ct);
                return Results.Ok(await client.GetPokemonAsync(
                    userId.Value, search, parsedTagIds, preferences.PokemonSpriteSource, skip ?? 0, take ?? 24, ct));
            }
        );

        group.MapGet("/tags", async (HttpContext context, IBeastVaultClient client, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await client.GetTagsAsync(userId.Value, ct));
        });

        group.MapGet("/sprites/{speciesId:int}", async (
            int speciesId,
            bool? shiny,
            string? source,
            HttpContext context,
            IBeastVaultClient client,
            IUserSettingsService settings,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            var preferences = await settings.GetPreferencesAsync(userId.Value, ct);
            var sprite = await client.GetSpriteAsync(userId.Value, speciesId, shiny ?? false, source ?? preferences.PokemonSpriteSource, ct);
            context.Response.Headers.CacheControl = "private, no-store";
            return sprite is null
                ? Results.NotFound()
                : Results.File(sprite.Value.Content, sprite.Value.ContentType, enableRangeProcessing: false);
        }).RequireRateLimiting("asset");

        group.MapGet("/tags/images/{fileName}", async (
            string fileName,
            HttpContext context,
            IBeastVaultClient client,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            var image = await client.GetTagImageAsync(userId.Value, fileName, ct);
            return image is null ? Results.NotFound() : Results.File(image.Value.Content, image.Value.ContentType, enableRangeProcessing: false);
        }).RequireRateLimiting("asset");

        group.MapGet("/{id:int}/download", async (
            int id,
            HttpContext context,
            IBeastVaultClient client,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            var file = await client.DownloadPokemonAsync(userId.Value, id, ct);
            return file is null
                ? Results.NotFound()
                : Results.File(file.Value.Content, file.Value.ContentType, file.Value.FileName, enableRangeProcessing: false);
        }).RequireRateLimiting("download");
    }

    private static bool TryParseTagIds(string? value, out IReadOnlyList<int> tagIds)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            tagIds = [];
            return true;
        }

        var parsed = new List<int>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var tagId) || tagId <= 0 || parsed.Count >= 25)
            {
                tagIds = [];
                return false;
            }
            parsed.Add(tagId);
        }

        tagIds = parsed.Distinct().ToList();
        return true;
    }
}
