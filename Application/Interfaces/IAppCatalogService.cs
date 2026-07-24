using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IAppCatalogService
{
    Task<IReadOnlyList<AppLauncherItemDto>> GetAppsAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<AppLauncherItemDto?> GetAppAsync(Guid userId, bool isAdmin, string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppLauncherCategoryDto>> GetCategoriesAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken);
    Task<AppLauncherItemDto?> SetFavoriteAsync(Guid userId, bool isAdmin, string id, bool favorite, CancellationToken cancellationToken);
}
