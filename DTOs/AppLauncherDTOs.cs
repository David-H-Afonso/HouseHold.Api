using Household.Api.Models.Integrations;

namespace Household.Api.DTOs;

public record AppLauncherItemDto(
    string Id,
    string Name,
    string Category,
    string? Description,
    string? IconUrl,
    string? OpenUrl,
    bool Favorite,
    IntegrationHealthStatus HealthStatus,
    string FrontStatus,
    string ApiStatus,
    string UserConnectionStatus,
    string ContainerStatus,
    string? Image,
    IReadOnlyList<string> Ports,
    DateTime? LastUpdated,
    bool? UpdateAvailable,
    bool AdminActionsAvailable
);

public record AppLauncherCategoryDto(string Name, int Count);

public record UpdateAppFavoriteRequest(bool Favorite);
