using Household.Api.Application.Interfaces;
using Household.Api.Helpers;

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
                CancellationToken ct
            ) =>
            {
                var userId = context.GetUserId();
                if (userId is null)
                    return Results.Unauthorized();
                if (!TryParseTagIds(tagIds, out var parsedTagIds))
                    return Results.BadRequest(new { message = "tag_ids_must_be_positive_integers" });

                return Results.Ok(
                    await client.GetPokemonAsync(userId.Value, search, parsedTagIds, skip ?? 0, take ?? 24, ct)
                );
            }
        );

        group.MapGet("/tags", async (HttpContext context, IBeastVaultClient client, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await client.GetTagsAsync(userId.Value, ct));
        });
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
