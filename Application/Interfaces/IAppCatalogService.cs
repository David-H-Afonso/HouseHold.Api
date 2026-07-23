using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IAppCatalogService
{
    Task<IReadOnlyList<AppLauncherItemDto>> GetAppsAsync(CancellationToken cancellationToken);
    Task<AppLauncherItemDto?> GetAppAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppLauncherCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<AppLauncherItemDto?> SetFavoriteAsync(string id, bool favorite, CancellationToken cancellationToken);
}
