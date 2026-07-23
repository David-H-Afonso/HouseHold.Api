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

    public AppCatalogService(
        AppDbContext db,
        IAppLauncherConfigLoader loader,
        IContainerStatusService containerStatusService
    )
    {
        _db = db;
        _loader = loader;
        _containerStatusService = containerStatusService;
    }

    public async Task<IReadOnlyList<AppLauncherItemDto>> GetAppsAsync(CancellationToken cancellationToken)
    {
        var configItems = await _loader.LoadAsync(cancellationToken);
        var favoriteOverrides = await _db
            .AppLauncherItems.AsNoTracking()
            .Where(item => item.Favorite)
            .Select(item => item.AppId)
            .ToListAsync(cancellationToken);

        var favorites = favoriteOverrides.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var results = new List<AppLauncherItemDto>();
        foreach (var item in configItems)
        {
            var containers = await _containerStatusService.GetAppContainersAsync(item.Id, cancellationToken);
            results.Add(ToDto(item, favorites.Contains(item.Id) || item.Favorite, containers));
        }

        return results.OrderByDescending(item => item.Favorite)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Name)
            .ToList();
    }

    public async Task<AppLauncherItemDto?> GetAppAsync(string id, CancellationToken cancellationToken)
    {
        var apps = await GetAppsAsync(cancellationToken);
        return apps.FirstOrDefault(app => string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<AppLauncherCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var apps = await GetAppsAsync(cancellationToken);
        return apps.GroupBy(app => app.Category)
            .Select(group => new AppLauncherCategoryDto(group.Key, group.Count()))
            .OrderBy(category => category.Name)
            .ToList();
    }

    public async Task<AppLauncherItemDto?> SetFavoriteAsync(
        string id,
        bool favorite,
        CancellationToken cancellationToken
    )
    {
        var configItems = await _loader.LoadAsync(cancellationToken);
        var configItem = configItems.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (configItem is null)
            return null;

        var existing = await _db.AppLauncherItems.FirstOrDefaultAsync(
            item => item.AppId.ToLower() == id.ToLower(),
            cancellationToken
        );

        if (existing is null)
        {
            existing = new AppLauncherItem
            {
                AppId = configItem.Id,
                Name = configItem.Name,
                Category = NormalizeCategory(configItem.Category),
                Description = configItem.Description,
                IconUrl = configItem.IconUrl,
                InternalUrl = configItem.InternalUrl,
                ExternalUrl = configItem.ExternalUrl,
                OpenUrl = configItem.OpenUrl,
            };
            _db.AppLauncherItems.Add(existing);
        }

        existing.Favorite = favorite;
        await _db.SaveChangesAsync(cancellationToken);

        var containers = await _containerStatusService.GetAppContainersAsync(configItem.Id, cancellationToken);
        return ToDto(configItem, favorite, containers);
    }

    private static AppLauncherItemDto ToDto(
        Infrastructure.AppLauncher.AppLauncherConfigItem item,
        bool favorite,
        IReadOnlyList<ContainerStatusDto> containers
    ) =>
        new(
            item.Id.Trim(),
            item.Name.Trim(),
            NormalizeCategory(item.Category),
            NormalizeOptional(item.Description),
            NormalizeOptional(item.IconUrl),
            NormalizeOptional(item.InternalUrl),
            NormalizeOptional(item.ExternalUrl),
            NormalizeOptional(item.OpenUrl) ?? NormalizeOptional(item.ExternalUrl) ?? NormalizeOptional(item.InternalUrl),
            favorite,
            IntegrationHealthStatus.Unknown,
            ResolveContainerStatus(containers),
            containers.FirstOrDefault(container => !string.IsNullOrWhiteSpace(container.Image))?.Image,
            containers.SelectMany(container => container.Ports).Distinct().OrderBy(port => port).ToList(),
            containers.Select(container => container.StartedAt).Where(value => value.HasValue).Max(),
            false
        );

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
}
