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
                async (
                    string? date,
                    HttpContext context,
                    IDoItClient client,
                    IUserSettingsService settings,
                    CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    if (userId is null)
                        return Results.Unauthorized();
                    if (!TryParseDate(date, out var requestedDate))
                        return Results.BadRequest(new { message = "date_must_use_yyyy_mm_dd" });

                    var preferences = await settings.GetPreferencesAsync(userId.Value, ct);
                    var timeZoneId = preferences.TimeZoneId ?? "UTC";
                    var explicitDate = requestedDate ?? GetLocalDate(timeZoneId);
                    return Results.Ok(await client.GetNowAsync(
                        userId.Value,
                        explicitDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        timeZoneId,
                        ct));
                }
            );

        group.MapPost("/occurrences/{occurrenceId:guid}/complete", async (
            Guid occurrenceId,
            string? date,
            HttpContext context,
            IDoItClient client,
            IUserSettingsService settings,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            if (!TryParseDate(date, out var requestedDate))
                return Results.BadRequest(new { message = "date_must_use_yyyy_mm_dd" });
            var preferences = await settings.GetPreferencesAsync(userId.Value, ct);
            var timeZoneId = preferences.TimeZoneId ?? "UTC";
            return Results.Ok(await client.CompleteOccurrenceAsync(
                userId.Value,
                occurrenceId,
                requestedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                timeZoneId,
                ct));
        }).RequireRateLimiting("mutation");

        group.MapPost("/occurrences/{occurrenceId:guid}/undo", async (
            Guid occurrenceId,
            string? date,
            HttpContext context,
            IDoItClient client,
            IUserSettingsService settings,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            if (!TryParseDate(date, out var requestedDate))
                return Results.BadRequest(new { message = "date_must_use_yyyy_mm_dd" });
            var preferences = await settings.GetPreferencesAsync(userId.Value, ct);
            var timeZoneId = preferences.TimeZoneId ?? "UTC";
            return Results.Ok(await client.UndoOccurrenceAsync(
                userId.Value,
                occurrenceId,
                requestedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                timeZoneId,
                ct));
        }).RequireRateLimiting("mutation");
    }

    private static bool TryParseDate(string? value, out DateOnly? date)
    {
        if (value is null)
        {
            date = null;
            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            return true;
        }

        date = null;
        return false;
    }

    private static DateOnly GetLocalDate(string timeZoneId) => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)));
}
