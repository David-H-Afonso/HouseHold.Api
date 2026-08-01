using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Infrastructure.Integrations.CasaOs;
using Household.Api.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public sealed class AppCatalogService(
    AppDbContext db,
    IContainerStatusService containerStatusService,
    IHttpClientFactory httpClientFactory,
    ICasaOsUpdateService casaOsUpdateService) : IAppCatalogService
{
    private static readonly SemaphoreSlim OperationalCallGate = new(4, 4);

    public async Task<IReadOnlyList<AppLauncherItemDto>> GetAppsAsync(
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var items = await db.AppLauncherItems.AsNoTracking()
            .Where(item => item.Enabled)
            .ToListAsync(cancellationToken);
        items = items.GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var policies = await db.AllowedComposeApps.AsNoTracking().ToListAsync(cancellationToken);
        var policiesById = policies.GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var favoriteOverrides = await db.UserAppFavorites.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new { item.AppId, item.Favorite })
            .ToListAsync(cancellationToken);
        var favorites = favoriteOverrides.GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Favorite, StringComparer.OrdinalIgnoreCase);
        var connections = await db.HouseholdConsumerConnections.AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .ToListAsync(cancellationToken);
        var providerStatuses = connections.GroupBy(
                connection => NormalizeProviderAppId(connection.Provider),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Status.ToString(), StringComparer.OrdinalIgnoreCase);
        var containerStatuses = (await containerStatusService.GetAllAppStatusesAsync(cancellationToken))
            .GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Containers, StringComparer.OrdinalIgnoreCase);
        var casaOsCapabilities = await RunBoundedOperationalCallAsync(
            () => casaOsUpdateService.GetAppCapabilitiesAsync(cancellationToken),
            cancellationToken);

        var tasks = items.Select(async item =>
        {
            policiesById.TryGetValue(item.AppId, out var policy);
            containerStatuses.TryGetValue(item.AppId, out var containers);
            containers ??= [];
            providerStatuses.TryGetValue(item.AppId, out var connectionStatus);
            casaOsCapabilities.UpdateAvailability.TryGetValue(item.AppId, out var updateAvailable);
            var monitoringEnabled = policy is not null;
            var frontStatus = monitoringEnabled
                ? await CheckHealthAsync(item.InternalUrl, cancellationToken)
                : "not_monitored";
            var apiStatus = monitoringEnabled
                ? await CheckHealthAsync(policy!.HealthCheckUrl, cancellationToken)
                : "not_monitored";
            var containerStatus = ResolveContainerStatus(containers);
            var canUpdate = isAdmin
                && casaOsCapabilities.Configured
                && policy?.AdminActionsEnabled == true
                && CasaOsUpdatePolicy.IsAllowedAppId(item.AppId);
            var favorite = favorites.TryGetValue(item.AppId, out var overrideValue)
                ? overrideValue
                : item.Favorite;
            return ToDto(
                item,
                favorite,
                containers,
                frontStatus,
                apiStatus,
                connectionStatus ?? "not_applicable",
                containerStatus,
                updateAvailable,
                monitoringEnabled,
                canUpdate);
        });

        var results = await Task.WhenAll(tasks);
        return results.OrderByDescending(item => item.Favorite)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Name)
            .ToList();
    }

    public async Task<AppLauncherItemDto?> GetAppAsync(
        Guid userId,
        bool isAdmin,
        string id,
        CancellationToken cancellationToken)
    {
        var normalizedId = id?.Trim() ?? string.Empty;
        var item = await db.AppLauncherItems.AsNoTracking()
            .Where(candidate => candidate.Enabled && candidate.AppId.ToLower() == normalizedId.ToLower())
            .OrderBy(candidate => candidate.AppId == normalizedId ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null) return null;
        var policy = await db.AllowedComposeApps.AsNoTracking()
            .Where(candidate => candidate.AppId.ToLower() == item.AppId.ToLower())
            .FirstOrDefaultAsync(cancellationToken);
        var favoriteOverride = await db.UserAppFavorites.AsNoTracking()
            .Where(candidate => candidate.UserId == userId && candidate.AppId.ToLower() == item.AppId.ToLower())
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Select(candidate => (bool?)candidate.Favorite)
            .FirstOrDefaultAsync(cancellationToken);
        var connections = await db.HouseholdConsumerConnections.AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .ToListAsync(cancellationToken);
        var connectionStatus = connections
            .FirstOrDefault(connection => string.Equals(
                NormalizeProviderAppId(connection.Provider),
                item.AppId,
                StringComparison.OrdinalIgnoreCase))
            ?.Status.ToString() ?? "not_applicable";
        var containers = policy is null
            ? []
            : await containerStatusService.GetAppContainersAsync(item.AppId, cancellationToken);
        var capabilities = await RunBoundedOperationalCallAsync(
            () => casaOsUpdateService.GetAppCapabilitiesAsync(cancellationToken),
            cancellationToken);
        capabilities.UpdateAvailability.TryGetValue(item.AppId, out var updateAvailable);
        var monitoringEnabled = policy is not null;
        var frontStatus = monitoringEnabled
            ? await CheckHealthAsync(item.InternalUrl, cancellationToken)
            : "not_monitored";
        var apiStatus = monitoringEnabled
            ? await CheckHealthAsync(policy!.HealthCheckUrl, cancellationToken)
            : "not_monitored";
        var containerStatus = ResolveContainerStatus(containers);
        var canUpdate = isAdmin
            && capabilities.Configured
            && policy?.AdminActionsEnabled == true
            && CasaOsUpdatePolicy.IsAllowedAppId(item.AppId);
        return ToDto(
            item,
            favoriteOverride ?? item.Favorite,
            containers,
            frontStatus,
            apiStatus,
            connectionStatus,
            containerStatus,
            updateAvailable,
            monitoringEnabled,
            canUpdate);
    }

    public async Task<IReadOnlyList<AppLauncherCategoryDto>> GetCategoriesAsync(
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        _ = userId;
        _ = isAdmin;
        var categories = await db.AppLauncherItems.AsNoTracking()
            .Where(item => item.Enabled)
            .Select(item => item.Category)
            .ToListAsync(cancellationToken);
        return categories.Select(NormalizeCategory)
            .GroupBy(category => category)
            .Select(group => new AppLauncherCategoryDto(group.Key, group.Count()))
            .OrderBy(category => category.Name)
            .ToList();
    }

    public async Task<AppLauncherItemDto?> SetFavoriteAsync(
        Guid userId,
        bool isAdmin,
        string id,
        bool favorite,
        CancellationToken cancellationToken)
    {
        var catalogId = await db.AppLauncherItems.AsNoTracking()
            .Where(item => item.Enabled && item.AppId.ToLower() == id.ToLower())
            .Select(item => item.AppId)
            .FirstOrDefaultAsync(cancellationToken);
        if (catalogId is null) return null;

        var existing = await db.UserAppFavorites
            .Where(item => item.UserId == userId && item.AppId.ToLower() == catalogId.ToLower())
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            existing = new UserAppFavorite { UserId = userId, AppId = catalogId };
            db.UserAppFavorites.Add(existing);
        }
        existing.Favorite = favorite;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAppAsync(userId, isAdmin, catalogId, cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAppCatalogItemDto>> GetAdminCatalogAsync(
        CancellationToken cancellationToken)
    {
        var policies = await db.AllowedComposeApps.AsNoTracking().ToListAsync(cancellationToken);
        var policiesById = policies.GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var items = await db.AppLauncherItems.AsNoTracking()
            .OrderBy(item => item.Category)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken);
        return items.Select(item => ToAdminDto(item, policiesById.GetValueOrDefault(item.AppId))).ToList();
    }

    public async Task<AdminAppCatalogItemDto?> UpdateCatalogItemAsync(
        string id,
        UpdateAppCatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await db.AppLauncherItems
            .Where(candidate => candidate.AppId.ToLower() == id.ToLower())
            .OrderBy(candidate => candidate.AppId == id ? 0 : 1)
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null) return null;

        item.Name = RequireText(request.Name, 160, "App name");
        item.Category = RequireText(request.Category, 120, "App category");
        item.Description = OptionalText(request.Description, 1000, "App description");
        item.IconUrl = ValidateBrowserUrl(request.IconUrl, true, "App icon URL");
        item.OpenUrl = ValidateBrowserUrl(request.OpenUrl, false, "App open URL");
        item.ExternalUrl = item.OpenUrl;
        item.Favorite = request.Favorite;
        item.Enabled = request.Enabled;
        await db.SaveChangesAsync(cancellationToken);

        var policy = await db.AllowedComposeApps.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.AppId.ToLower() == item.AppId.ToLower(), cancellationToken);
        return ToAdminDto(item, policy);
    }

    private static AppLauncherItemDto ToDto(
        AppLauncherItem item,
        bool favorite,
        IReadOnlyList<ContainerStatusDto> containers,
        string frontStatus,
        string apiStatus,
        string userConnectionStatus,
        string containerStatus,
        bool? updateAvailable,
        bool monitoringEnabled,
        bool canUpdate) =>
        new(
            item.AppId.Trim(),
            item.Name.Trim(),
            NormalizeCategory(item.Category),
            NormalizeOptional(item.Description),
            NormalizeBrowserUrl(item.IconUrl, true),
            NormalizeBrowserUrl(item.OpenUrl, false),
            favorite,
            ResolveHealth(monitoringEnabled, frontStatus, apiStatus, containerStatus),
            frontStatus,
            apiStatus,
            userConnectionStatus,
            containerStatus,
            containers.FirstOrDefault(container => !string.IsNullOrWhiteSpace(container.Image))?.Image,
            containers.SelectMany(container => container.Ports).Distinct().OrderBy(port => port).ToList(),
            containers.Select(container => container.StartedAt).Where(value => value.HasValue).Max(),
            updateAvailable,
            canUpdate,
            monitoringEnabled,
            canUpdate,
            false);

    private static AdminAppCatalogItemDto ToAdminDto(AppLauncherItem item, AllowedComposeApp? policy) => new(
        item.AppId,
        item.Name,
        item.Category,
        item.Description,
        item.IconUrl,
        item.OpenUrl,
        item.Favorite,
        item.Enabled,
        policy is not null,
        policy?.AdminActionsEnabled == true && CasaOsUpdatePolicy.IsAllowedAppId(item.AppId),
        false,
        item.UpdatedAt);

    private async Task<string> CheckHealthAsync(string? url, CancellationToken cancellationToken)
    {
        if (!TryNormalizeInternalUrl(url, out var normalized)) return "not_configured";
        using var request = new HttpRequestMessage(HttpMethod.Get, normalized);
        try
        {
            using var response = await RunBoundedOperationalCallAsync(
                () => httpClientFactory.CreateClient("AppHealth").SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken),
                cancellationToken);
            return response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and <= 399
                ? "healthy"
                : "degraded";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return "offline";
        }
    }

    private static async Task<T> RunBoundedOperationalCallAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await OperationalCallGate.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            OperationalCallGate.Release();
        }
    }

    private static IntegrationHealthStatus ResolveHealth(
        bool monitoringEnabled,
        string frontStatus,
        string apiStatus,
        string containerStatus)
    {
        if (!monitoringEnabled) return IntegrationHealthStatus.Unknown;
        if (frontStatus == "offline" || apiStatus == "offline") return IntegrationHealthStatus.Offline;
        if (containerStatus is not ("running" or "unknown")) return IntegrationHealthStatus.Offline;
        if (frontStatus == "degraded" || apiStatus == "degraded") return IntegrationHealthStatus.Degraded;
        var hasHealthyCheck = frontStatus == "healthy" || apiStatus == "healthy";
        if (containerStatus == "running" && (hasHealthyCheck || frontStatus == "not_configured" && apiStatus == "not_configured"))
            return IntegrationHealthStatus.Healthy;
        return IntegrationHealthStatus.Unknown;
    }

    private static string ResolveContainerStatus(IReadOnlyList<ContainerStatusDto> containers)
    {
        if (containers.Count == 0) return "unknown";
        var statuses = containers.Select(container => container.Status.ToLowerInvariant()).ToList();
        if (statuses.Any(status => status is not ("running" or "unknown")))
            return statuses.First(status => status is not ("running" or "unknown"));
        return statuses.All(status => status == "running") ? "running" : "unknown";
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

    private static string NormalizeProviderAppId(string provider) => provider switch
    {
        "games-database" => "gamesdatabase",
        "beast-vault" => "beastvault",
        "warcraft-archive" => "warcraftarchive",
        _ => provider,
    };

    private static string RequireText(string? value, int maxLength, string field)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is < 1 || candidate.Length > maxLength || candidate.Any(char.IsControl))
            throw new ArgumentException($"{field} is invalid.");
        return candidate;
    }

    private static string? OptionalText(string? value, int maxLength, string field)
    {
        var candidate = NormalizeOptional(value);
        if (candidate is not null && (candidate.Length > maxLength || candidate.Any(char.IsControl)))
            throw new ArgumentException($"{field} is invalid.");
        return candidate;
    }

    private static string? ValidateBrowserUrl(string? value, bool allowRelative, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return NormalizeBrowserUrl(value, allowRelative)
            ?? throw new ArgumentException($"{field} is invalid.");
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
