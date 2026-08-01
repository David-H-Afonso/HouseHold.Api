using Household.Api.Application.Interfaces;
using Household.Api.DTOs;
using Household.Api.Helpers;

namespace Household.Api.Endpoints;

public static class AppsModuleEndpoints
{
    public static void MapAppsModuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/modules/apps").WithTags("Apps").RequireAuthorization().RequireRateLimiting("app-read");

        group
            .MapGet("/", async (HttpContext context, IAppCatalogService service, CancellationToken ct) =>
            {
                var userId = context.GetUserId();
                return userId is null
                    ? Results.Unauthorized()
                    : Results.Ok(await service.GetAppsAsync(userId.Value, context.IsAdmin(), ct));
            }
            )
            .WithName("GetApps")
            .WithSummary("List app launcher entries from the mounted app launcher config");

        group
            .MapGet(
                "/categories",
                async (HttpContext context, IAppCatalogService service, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    return userId is null
                        ? Results.Unauthorized()
                        : Results.Ok(await service.GetCategoriesAsync(userId.Value, context.IsAdmin(), ct));
                }
            )
            .WithName("GetAppCategories")
            .WithSummary("List app launcher categories");

        group
            .MapGet(
                "/{id}",
                async (string id, HttpContext context, IAppCatalogService service, CancellationToken ct) =>
                {
                    var userId = context.GetUserId();
                    if (userId is null) return Results.Unauthorized();
                    var appItem = await service.GetAppAsync(userId.Value, context.IsAdmin(), id, ct);
                    return appItem is null ? Results.NotFound() : Results.Ok(appItem);
                }
            )
            .WithName("GetApp")
            .WithSummary("Get one app launcher entry");

        group
            .MapGet(
                "/{id}/containers",
                async (string id, IContainerStatusService service, CancellationToken ct) =>
                    Results.Ok(await service.GetAppContainersAsync(id, ct))
            )
            .WithName("GetAppContainers")
            .WithSummary("Read-only container status for an allowlisted app launcher entry");

        group
            .MapGet(
                "/status",
                async (IContainerStatusService service, CancellationToken ct) =>
                    Results.Ok(await service.GetAllAppStatusesAsync(ct))
            )
            .WithName("GetAppContainerStatuses")
            .WithSummary("Read-only container status for all configured launcher entries");

        group
            .MapPut(
                "/{id}/favorite",
                async (
                    string id,
                    UpdateAppFavoriteRequest request,
                    HttpContext context,
                    IAppCatalogService service,
                    CancellationToken ct
                ) =>
                {
                    var userId = context.GetUserId();
                    if (userId is null) return Results.Unauthorized();
                    var appItem = await service.SetFavoriteAsync(
                        userId.Value,
                        context.IsAdmin(),
                        id,
                        request.Favorite,
                        ct
                    );
                    return appItem is null ? Results.NotFound() : Results.Ok(appItem);
                }
            )
            .WithName("SetAppFavorite")
            .WithSummary("Set the current user's favorite for an app launcher entry")
            .RequireRateLimiting("mutation");

        var admin = app.MapGroup("/api/v1/admin/apps/catalog")
            .WithTags("Apps Admin")
            .RequireAuthorization()
            .RequireRateLimiting("admin");

        admin.MapGet("/", async (
            HttpContext context,
            IAppCatalogService service,
            CancellationToken ct) =>
            context.IsAdmin() ? Results.Ok(await service.GetAdminCatalogAsync(ct)) : Results.Forbid());

        admin.MapPatch("/{id}", async (
            string id,
            UpdateAppCatalogItemRequest request,
            HttpContext context,
            IAppCatalogService service,
            CancellationToken ct) =>
        {
            if (!context.IsAdmin()) return Results.Forbid();
            var result = await service.UpdateCatalogItemAsync(id, request, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireRateLimiting("mutation");
    }
}
