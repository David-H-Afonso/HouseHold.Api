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
    bool AdminActionsAvailable,
    bool MonitoringEnabled,
    bool CanUpdate,
    bool CanRollback
);

public record AppLauncherCategoryDto(string Name, int Count);

public record UpdateAppFavoriteRequest(bool Favorite);

public sealed record AdminAppCatalogItemDto(
    string Id,
    string Name,
    string Category,
    string? Description,
    string? IconUrl,
    string? OpenUrl,
    bool Favorite,
    bool Enabled,
    bool MonitoringEnabled,
    bool CanUpdate,
    bool CanRollback,
    DateTime UpdatedAt
);

public sealed record UpdateAppCatalogItemRequest(
    string Name,
    string Category,
    string? Description,
    string? IconUrl,
    string? OpenUrl,
    bool Favorite,
    bool Enabled
);
