using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class SeerrEndpoints
{
    public static void MapSeerrEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/seerr").WithTags("Seerr").RequireAuthorization();

        // ── Admin configuration ──────────────────────────────────────────────────
        group.MapGet("/config", async (HttpContext context, ISeerrService service, CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.GetConfigAsync(ct)) : Results.Forbid());

        group.MapPut("/config", async (
            UpdateSeerrConfigRequest request,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.UpdateConfigAsync(request, ct)) : Results.Forbid()
        ).RequireRateLimiting("admin");

        group.MapGet("/users/mappings", async (
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.GetUserMappingsAsync(ct)) : Results.Forbid());

        group.MapPut("/users/{userId:guid}/mapping", async (
            Guid userId,
            UpdateSeerrUserMappingRequest request,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            if (!context.IsAdmin() || context.GetUserId() is not Guid actorUserId) return Results.Forbid();
            return Results.Ok(await service.UpdateUserMappingAsync(actorUserId, userId, request, ct));
        }).RequireRateLimiting("admin");

        group.MapDelete("/users/{userId:guid}/mapping", async (
            Guid userId,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            if (!context.IsAdmin() || context.GetUserId() is not Guid actorUserId) return Results.Forbid();
            await service.ClearUserMappingAsync(actorUserId, userId, ct);
            return Results.NoContent();
        }).RequireRateLimiting("admin");

        // ── Session ──────────────────────────────────────────────────────────────
        group.MapGet("/session", async (HttpContext context, ISeerrService service, CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.GetSessionAsync(userId.Value, ct));
        }).RequireRateLimiting("seerr-read");

        // ── Discovery / search ───────────────────────────────────────────────────
        group.MapGet("/search", async (
            string query,
            int? page,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.SearchAsync(userId.Value, query, page ?? 1, ct));
        }).RequireRateLimiting("seerr-read");

        group.MapGet("/discover", async (
            string? kind,
            int? page,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.DiscoverAsync(userId.Value, kind ?? "trending", page ?? 1, ct));
        }).RequireRateLimiting("seerr-read");

        group.MapGet("/movie/{tmdbId:int}", async (
            int tmdbId,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.GetMovieAsync(userId.Value, tmdbId, ct));
        }).RequireRateLimiting("seerr-read");

        group.MapGet("/tv/{tmdbId:int}", async (
            int tmdbId,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.GetTvAsync(userId.Value, tmdbId, ct));
        }).RequireRateLimiting("seerr-read");

        // ── Requests ─────────────────────────────────────────────────────────────
        group.MapGet("/requests", async (
            string? filter,
            bool? mine,
            int? page,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null
                ? Results.Unauthorized()
                : Results.Ok(await service.GetRequestsAsync(userId.Value, filter ?? "all", mine ?? true, page ?? 1, ct));
        }).RequireRateLimiting("seerr-read");

        group.MapPost("/requests", async (
            CreateSeerrRequestBody body,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            return userId is null ? Results.Unauthorized() : Results.Ok(await service.CreateRequestAsync(userId.Value, body, ct));
        }).RequireRateLimiting("seerr-mutation");

        group.MapPost("/requests/{id:int}/{action}", async (
            int id,
            string action,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            await service.ModerateRequestAsync(userId.Value, id, action, ct);
            return Results.NoContent();
        }).RequireRateLimiting("seerr-mutation");

        group.MapDelete("/requests/{id:int}", async (
            int id,
            HttpContext context,
            ISeerrService service,
            CancellationToken ct) =>
        {
            var userId = context.GetUserId();
            if (userId is null) return Results.Unauthorized();
            await service.DeleteRequestAsync(userId.Value, id, ct);
            return Results.NoContent();
        }).RequireRateLimiting("seerr-mutation");
    }
}
