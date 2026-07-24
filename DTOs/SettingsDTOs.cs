namespace Household.Api.DTOs;

public sealed record UserPreferencesDto(
    int SchemaVersion,
    string? TimeZoneId,
    string VisualPreference,
    string PokemonSpriteSource,
    IReadOnlyList<int> GamesStatusOrder,
    IReadOnlyList<string> HiddenGitHubRepos,
    string? JellyfinUserId
);

public sealed record UpdateUserPreferencesRequest(
    int SchemaVersion,
    string? TimeZoneId,
    string? VisualPreference,
    string? PokemonSpriteSource,
    IReadOnlyList<int>? GamesStatusOrder,
    IReadOnlyList<string>? HiddenGitHubRepos,
    string? JellyfinUserId,
    bool ClearJellyfinUserId = false
);

public sealed record DashboardWidgetCatalogItemDto(
    string Type,
    string Name,
    string DefaultSize,
    IReadOnlyList<string> AllowedSizes,
    bool DefaultVisible
);

public sealed record DashboardLayoutItemDto(
    string Type,
    int Position,
    bool Visible,
    string Size,
    string? SettingsJson
);

public sealed record DashboardLayoutDto(int SchemaVersion, IReadOnlyList<DashboardLayoutItemDto> Widgets);
public sealed record UpdateDashboardLayoutRequest(int SchemaVersion, IReadOnlyList<DashboardLayoutItemDto> Widgets);
