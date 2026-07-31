using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public class AppCatalogService : IAppCatalogService
{
    private readonly AppDbContext _db;
    private readonly IAppLauncherConfigLoader _loader;
    private readonly IContainerStatusService _containerStatusService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICasaOsUpdateService _casaOsUpdateService;

    public AppCatalogService(
        AppDbContext db,
        IAppLauncherConfigLoader loader,
        IContainerStatusService containerStatusService,
        IHttpClientFactory httpClientFactory,
        ICasaOsUpdateService casaOsUpdateService
    )
    {
        _db = db;
        _loader = loader;
        _containerStatusService = containerStatusService;
        _httpClientFactory = httpClientFactory;
        _casaOsUpdateService = casaOsUpdateService;
    }

    public async Task<IReadOnlyList<AppLauncherItemDto>> GetAppsAsync(
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken
    )
    {
        var configItems = await _loader.LoadAsync(cancellationToken);
        var casaOsCapabilities = await _casaOsUpdateService.GetAppCapabilitiesAsync(cancellationToken);
        var favoriteOverrides = await _db
            .UserAppFavorites.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new { item.AppId, item.Favorite })
            .ToListAsync(cancellationToken);

        var favorites = favoriteOverrides.ToDictionary(item => item.AppId, item => item.Favorite, StringComparer.OrdinalIgnoreCase);
        var connections = await _db.HouseholdConsumerConnections.AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .ToListAsync(cancellationToken);
        var providerStatuses = connections.ToDictionary(
            connection => NormalizeProviderAppId(connection.Provider),
            connection => connection.Status.ToString(),
            StringComparer.OrdinalIgnoreCase
        );

        var results = new List<AppLauncherItemDto>();
        foreach (var item in configItems)
        {
            var containers = await _containerStatusService.GetAppContainersAsync(item.Id, cancellationToken);
            var apiStatus = await CheckHealthAsync(item.HealthCheckUrl, cancellationToken);
            var frontStatus = await CheckHealthAsync(item.InternalUrl, cancellationToken);
            providerStatuses.TryGetValue(item.Id, out var connectionStatus);
            var favorite = favorites.TryGetValue(item.Id, out var overrideValue) ? overrideValue : item.Favorite;
            casaOsCapabilities.UpdateAvailability.TryGetValue(item.Id, out var updateAvailable);
            results.Add(ToDto(
                item,
                favorite,
                containers,
                frontStatus,
                apiStatus,
                connectionStatus ?? "not_applicable",
                updateAvailable,
                isAdmin && casaOsCapabilities.Configured && CasaOsUpdatePolicy.IsAllowedAppId(item.Id)
            ));
        }

        return results.OrderByDescending(item => item.Favorite)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Name)
            .ToList();
    }

    public async Task<AppLauncherItemDto?> GetAppAsync(
        Guid userId,
        bool isAdmin,
        string id,
        CancellationToken cancellationToken
    )
    {
        var apps = await GetAppsAsync(userId, isAdmin, cancellationToken);
        return apps.FirstOrDefault(app => string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<AppLauncherCategoryDto>> GetCategoriesAsync(
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken
    )
    {
        var apps = await GetAppsAsync(userId, isAdmin, cancellationToken);
        return apps.GroupBy(app => app.Category)
            .Select(group => new AppLauncherCategoryDto(group.Key, group.Count()))
            .OrderBy(category => category.Name)
            .ToList();
    }

    public async Task<AppLauncherItemDto?> SetFavoriteAsync(
        Guid userId,
        bool isAdmin,
        string id,
        bool favorite,
        CancellationToken cancellationToken
    )
    {
        var configItems = await _loader.LoadAsync(cancellationToken);
        var configItem = configItems.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (configItem is null)
            return null;

        var existing = await _db.UserAppFavorites.FirstOrDefaultAsync(
            item => item.UserId == userId && item.AppId.ToLower() == id.ToLower(),
            cancellationToken
        );

        if (existing is null)
        {
            existing = new UserAppFavorite
            {
                UserId = userId,
                AppId = configItem.Id,
            };
            _db.UserAppFavorites.Add(existing);
        }

        existing.Favorite = favorite;
        await _db.SaveChangesAsync(cancellationToken);

        var containers = await _containerStatusService.GetAppContainersAsync(configItem.Id, cancellationToken);
        var casaOsCapabilities = await _casaOsUpdateService.GetAppCapabilitiesAsync(cancellationToken);
        casaOsCapabilities.UpdateAvailability.TryGetValue(configItem.Id, out var updateAvailable);
        return ToDto(
            configItem,
            favorite,
            containers,
            await CheckHealthAsync(configItem.InternalUrl, cancellationToken),
            await CheckHealthAsync(configItem.HealthCheckUrl, cancellationToken),
            "unknown",
            updateAvailable,
            isAdmin && casaOsCapabilities.Configured && CasaOsUpdatePolicy.IsAllowedAppId(configItem.Id)
        );
    }

    private static AppLauncherItemDto ToDto(
        Infrastructure.AppLauncher.AppLauncherConfigItem item,
        bool favorite,
        IReadOnlyList<ContainerStatusDto> containers,
        string frontStatus,
        string apiStatus,
        string userConnectionStatus,
        bool? updateAvailable,
        bool adminActionsAvailable
    ) =>
        new(
            item.Id.Trim(),
            item.Name.Trim(),
            NormalizeCategory(item.Category),
            NormalizeOptional(item.Description),
            NormalizeBrowserUrl(item.IconUrl, allowRelative: true),
            NormalizeBrowserUrl(item.OpenUrl, allowRelative: false),
            favorite,
            apiStatus == "healthy" && frontStatus == "healthy" ? IntegrationHealthStatus.Healthy : IntegrationHealthStatus.Degraded,
            frontStatus,
            apiStatus,
            userConnectionStatus,
            ResolveContainerStatus(containers),
            containers.FirstOrDefault(container => !string.IsNullOrWhiteSpace(container.Image))?.Image,
            containers.SelectMany(container => container.Ports).Distinct().OrderBy(port => port).ToList(),
            containers.Select(container => container.StartedAt).Where(value => value.HasValue).Max(),
            updateAvailable,
            adminActionsAvailable
        );

    private static string NormalizeProviderAppId(string provider) => provider switch
    {
        "games-database" => "gamesdatabase",
        "beast-vault" => "beastvault",
        "warcraft-archive" => "warcraftarchive",
        _ => provider,
    };

    private async Task<string> CheckHealthAsync(string? url, CancellationToken cancellationToken)
    {
        if (!TryNormalizeInternalUrl(url, out var normalized)) return "not_configured";
        using var request = new HttpRequestMessage(HttpMethod.Get, normalized);
        try
        {
            using var response = await _httpClientFactory.CreateClient("AppHealth").SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode ? "healthy" : "degraded";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return "offline";
        }
    }

    private static bool TryNormalizeInternalUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        normalized = uri.ToString();
        return true;
    }

    private static string ResolveContainerStatus(IReadOnlyList<ContainerStatusDto> containers)
    {
        if (containers.Count == 0)
            return "unknown";

        if (containers.Any(container => container.Status.Equals("running", StringComparison.OrdinalIgnoreCase)))
            return "running";

        if (containers.Any(container => container.Status.Equals("unknown", StringComparison.OrdinalIgnoreCase)))
            return "unknown";

        return containers[0].Status;
    }

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? NormalizeBrowserUrl(string? value, bool allowRelative)
    {
        var candidate = NormalizeOptional(value);
        if (candidate is null) return null;
        if (allowRelative && candidate[0] == '/')
            return !candidate.StartsWith("//", StringComparison.Ordinal)
                && !candidate.Contains('\\')
                && !candidate.Any(char.IsControl)
                    ? candidate
                    : null;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo)
                ? uri.ToString()
                : null;
    }
}
