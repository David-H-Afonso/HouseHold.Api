using Household.Api.Application.Interfaces;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class JellywatchModuleEndpoints
{
    public static void MapJellywatchModuleEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/modules/media/jellywatch",
                async (HttpContext context, IJellywatchClient client, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    return userId is null
                        ? Results.Unauthorized()
                        : Results.Ok(await client.GetDashboardAsync(userId.Value, ct));
                }
            )
            .WithTags("Media")
            .RequireAuthorization();
    }
}
