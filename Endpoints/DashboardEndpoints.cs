using Household.Api.Application.Interfaces;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization();

        group
            .MapGet(
                "/",
                async (IDashboardAggregationService dashboardService, HttpContext ctx, CancellationToken ct) =>
                {
                    var userId = ctx.GetUserId();
                    if (userId == null)
                        return Results.Unauthorized();

                    return Results.Ok(await dashboardService.GetDashboardAsync(userId.Value, ct));
                }
            )
            .WithName("GetDashboard")
            .WithSummary("Get the current user's dashboard shell data");
    }
}
