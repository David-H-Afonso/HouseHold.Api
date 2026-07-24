using System.Globalization;
using Household.Api.Application.Interfaces;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class DoItModuleEndpoints
{
    public static void MapDoItModuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/modules/today").WithTags("Today").RequireAuthorization();

        group.MapGet(
                "/",
                async (string? date, HttpContext context, IDoItClient client, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    if (userId is null)
                        return Results.Unauthorized();
                    if (
                        date is not null
                        && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    )
                        return Results.BadRequest(new { message = "date_must_use_yyyy_mm_dd" });

                    return Results.Ok(await client.GetNowAsync(userId.Value, date, ct));
                }
            );

        group.MapPost("/occurrences/{occurrenceId:guid}/complete", async (
            Guid occurrenceId,
            HttpContext context,
            IDoItClient client,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await client.CompleteOccurrenceAsync(userId.Value, occurrenceId, ct));
        });

        group.MapPost("/occurrences/{occurrenceId:guid}/undo", async (
            Guid occurrenceId,
            HttpContext context,
            IDoItClient client,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await client.UndoOccurrenceAsync(userId.Value, occurrenceId, ct));
        });
    }
}
