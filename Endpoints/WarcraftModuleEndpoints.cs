using Household.Api.Application.Interfaces;
using Household.Api.Helpers;

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
    }
}
