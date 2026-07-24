using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IUserSettingsService
{
    Task<UserPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserPreferencesDto> UpdatePreferencesAsync(Guid userId, UpdateUserPreferencesRequest request, CancellationToken cancellationToken);
    IReadOnlyList<DashboardWidgetCatalogItemDto> GetWidgetCatalog();
    Task<DashboardLayoutDto> GetLayoutAsync(Guid userId, CancellationToken cancellationToken);
    Task<DashboardLayoutDto> UpdateLayoutAsync(Guid userId, UpdateDashboardLayoutRequest request, CancellationToken cancellationToken);
    Task<DashboardLayoutDto> ResetLayoutAsync(Guid userId, CancellationToken cancellationToken);
}
