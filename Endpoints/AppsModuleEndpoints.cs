using Household.Api.Application.Interfaces;
using Household.Api.DTOs;

namespace Household.Api.Endpoints;

public static class AppsModuleEndpoints
{
    public static void MapAppsModuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/modules/apps").WithTags("Apps").RequireAuthorization();

        group
            .MapGet("/", async (IAppCatalogService service, CancellationToken ct) =>
                Results.Ok(await service.GetAppsAsync(ct))
            )
            .WithName("GetApps")
            .WithSummary("List app launcher entries from the mounted app launcher config");

        group
            .MapGet(
                "/categories",
                async (IAppCatalogService service, CancellationToken ct) =>
                    Results.Ok(await service.GetCategoriesAsync(ct))
            )
            .WithName("GetAppCategories")
            .WithSummary("List app launcher categories");

        group
            .MapGet(
                "/{id}",
                async (string id, IAppCatalogService service, CancellationToken ct) =>
                {
                    var appItem = await service.GetAppAsync(id, ct);
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
                    IAppCatalogService service,
                    CancellationToken ct
                ) =>
                {
                    var appItem = await service.SetFavoriteAsync(id, request.Favorite, ct);
                    return appItem is null ? Results.NotFound() : Results.Ok(appItem);
                }
            )
            .WithName("SetAppFavorite")
            .WithSummary("Set a local Household favorite override for an app launcher entry");
    }
}
