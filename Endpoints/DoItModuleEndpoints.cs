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
                    string? timeZoneId,
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
                    if (!TryResolveTimeZoneId(timeZoneId, preferences.TimeZoneId, out var resolvedTimeZoneId))
                        return Results.BadRequest(new { message = "invalid_time_zone" });
                    var explicitDate = requestedDate ?? GetLocalDate(resolvedTimeZoneId);
                    return Results.Ok(await client.GetNowAsync(
                        userId.Value,
                        explicitDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        resolvedTimeZoneId,
                        ct));
                }
            );

        group.MapPost("/occurrences/{occurrenceId:guid}/complete", async (
            Guid occurrenceId,
            string? date,
            string? timeZoneId,
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
            if (!TryResolveTimeZoneId(timeZoneId, preferences.TimeZoneId, out var resolvedTimeZoneId))
                return Results.BadRequest(new { message = "invalid_time_zone" });
            return Results.Ok(await client.CompleteOccurrenceAsync(
                userId.Value,
                occurrenceId,
                requestedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                resolvedTimeZoneId,
                ct));
        }).RequireRateLimiting("mutation");

        group.MapPost("/occurrences/{occurrenceId:guid}/undo", async (
            Guid occurrenceId,
            string? date,
            string? timeZoneId,
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
            if (!TryResolveTimeZoneId(timeZoneId, preferences.TimeZoneId, out var resolvedTimeZoneId))
                return Results.BadRequest(new { message = "invalid_time_zone" });
            return Results.Ok(await client.UndoOccurrenceAsync(
                userId.Value,
                occurrenceId,
                requestedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                resolvedTimeZoneId,
                ct));
        }).RequireRateLimiting("mutation");

        var calendarGroup = app.MapGroup("/modules/calendar").WithTags("Calendar").RequireAuthorization();
        calendarGroup.MapGet("/events", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            IDoItClient client,
            HttpContext context,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await client.GetCalendarEventsAsync(userId.Value, from, to, ct));
        });
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

    private static bool TryResolveTimeZoneId(string? requested, string? stored, out string timeZoneId)
    {
        timeZoneId = !string.IsNullOrWhiteSpace(requested)
            ? requested.Trim()
            : !string.IsNullOrWhiteSpace(stored) ? stored.Trim() : "UTC";
        if (timeZoneId.Length > 100 || timeZoneId.Any(char.IsControl)) return false;

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
