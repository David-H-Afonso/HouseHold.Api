using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class CasaOsUpdateEndpoints
{
    public static void MapCasaOsUpdateEndpoints(this WebApplication app)
    {
        var group = app
            .MapGroup("/api/v1/admin/casaos")
            .WithTags("CasaOS Admin Operations")
            .RequireAuthorization()
            .RequireRateLimiting("admin");

        group.MapGet(
            "/config",
            async (HttpContext context, ICasaOsUpdateService service, CancellationToken ct) =>
                context.IsAdmin() ? Results.Ok(await service.GetConfigAsync(ct)) : Results.Forbid()
        );

        group.MapPut(
            "/config",
            async (
                UpdateCasaOsUpdateConfigRequest request,
                HttpContext context,
                ICasaOsUpdateService service,
                CancellationToken ct
            ) => context.IsAdmin() ? Results.Ok(await service.UpdateConfigAsync(request, ct)) : Results.Forbid()
        );

        group
            .MapPost(
                "/apps/{appId}/update",
                async (
                    string appId,
                    HttpContext context,
                    ICasaOsUpdateService service,
                    CancellationToken ct
                ) =>
                {
                    if (!context.IsAdmin() || context.GetUserId() is not Guid actorUserId)
                        return Results.Forbid();
                    var result = await service.QueueUpdateAsync(actorUserId, appId, ct);
                    return Results.Accepted(value: result);
                }
            )
            .RequireRateLimiting("casaos-admin-action");

        group
            .MapPost(
                "/apps/{appId}/rollback",
                async (
                    string appId,
                    CasaOsRollbackRequest request,
                    HttpContext context,
                    ICasaOsUpdateService service,
                    CancellationToken ct
                ) =>
                {
                    if (!context.IsAdmin() || context.GetUserId() is not Guid actorUserId)
                        return Results.Forbid();
                    var result = await service.QueueRollbackAsync(
                        actorUserId,
                        appId,
                        request.Confirmation,
                        request.BackupId,
                        ct
                    );
                    return Results.Accepted(value: result);
                }
            )
            .RequireRateLimiting("casaos-admin-action");

        group.MapGet(
            "/apps/{appId}/actions",
            async (
                string appId,
                HttpContext context,
                ICasaOsUpdateService service,
                CancellationToken ct
            ) => context.IsAdmin() ? Results.Ok(await service.GetHistoryAsync(appId, ct)) : Results.Forbid()
        );

        group.MapGet(
            "/apps/{appId}/actions/{actionLogId:guid}",
            async (
                string appId,
                Guid actionLogId,
                HttpContext context,
                ICasaOsUpdateService service,
                CancellationToken ct
            ) =>
            {
                if (!context.IsAdmin())
                    return Results.Forbid();
                var result = await service.GetStatusAsync(appId, actionLogId, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
        );
    }
}
