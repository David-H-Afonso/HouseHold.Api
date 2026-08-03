using System.Text.Json;
using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.Infrastructure.AppLauncher;
using Household.Api.Infrastructure.Integrations.CasaOs;
using Household.Api.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public sealed class AppCatalogBootstrapper(
    AppDbContext db,
    IAppLauncherConfigLoader loader,
    ILogger<AppCatalogBootstrapper> logger)
{
    private static readonly CatalogSeed[] Catalog =
    [
        App("household", "Household", "Core", "Central home dashboard", "https://household.davidhormigafonso.work", true,
            "household", ["household-api", "household-front"], "http://household-api:8080/health"),
        App("doit", "DoIt", "Productivity", "Task planning and routines", "https://doit.davidhormigafonso.work", true,
            "doit", ["doit-api", "doit-web"], "http://doit-api:8080/api/health"),
        App("gamesdatabase", "Games Database", "Games", "Personal game collection", "https://gamesdatabase.davidhormigafonso.work", true,
            "gamesdatabase", ["gamesdatabase-api", "gamesdatabase-web"], "http://gamesdatabase-api:8080/health"),
        App("jellywatch", "Jellywatch", "Media", "Watch tracking and ratings", "https://jellywatch.davidhormigafonso.work", true,
            "jellywatch", ["jellywatch-api", "jellywatch-web"], "http://jellywatch-api:8080/health"),
        App("beastvault", "Beast Vault", "Collections", "Pokemon collection manager", "https://beastvault.davidhormigafonso.work", true,
            "beastvault", ["beastvault-api", "beastvault-web"], "http://beastvault-api:8080/health"),
        App("warcraftarchive", "Warcraft Archive", "Collections", "World of Warcraft progress tracker", "https://warcraftarchive.davidhormigafonso.work", true,
            "warcraftarchive", ["warcraftarchive-api", "warcraftarchive-front"], "http://warcraftarchive-api:8080/health"),
        App("portafolio", "Portafolio", "Development", "Personal portfolio", "https://portafolio.davidhormigafonso.work", true,
            "portafolio", ["portafolio"]),
        Link("casaos", "CasaOS", "System", "CasaOS server dashboard", "http://192.168.0.32", true),
        App("jellyfin", "Jellyfin", "Media", "Media server", "https://jellyfin.davidhormigafonso.work", true,
            "jellyfin", ["jellyfin"], "http://jellyfin:8096/System/Ping"),
        App("seerr", "Seerr", "Media", "Movie and series requests", "https://seerr.davidhormigafonso.work", true,
            "big-bear-seerr", ["seerr"], "http://seerr:5055/api/v1/status"),
        App("qbittorrent", "qBittorrent", "Downloads", "Torrent download client", "https://qbittorrent.davidhormigafonso.work", false,
            "qbittorrent", ["qbittorrent"]),
        App("sonarr", "Sonarr", "Media", "Series automation", "https://sonarr.davidhormigafonso.work", false,
            "sonarr", ["sonarr"]),
        App("radarr", "Radarr", "Media", "Movie automation", "https://radarr.davidhormigafonso.work", false,
            "radarr", ["radarr"]),
        Monitor("immich", "Immich", "Photos", "Photo and video library", "https://immich.davidhormigafonso.work", false,
            "big-bear-immich", ["immich_server", "immich_machine_learning", "immich_postgres", "immich_redis"]),
        App("komga", "Komga", "Reading", "Comics and manga server", "https://komga.davidhormigafonso.work", false,
            "komga", ["komga"]),
        App("wg-easy", "WireGuard Easy", "Network", "WireGuard VPN management", "https://wireguard.davidhormigafonso.work", false,
            "wg-easy", ["wg-easy"]),
        App("audiobookshelf", "Audiobookshelf", "Media", "Audiobook and podcast library", "https://audiobookshelf.davidhormigafonso.work", false,
            "big-bear-audiobookshelf", ["audiobookshelf"]),
        App("syncthing", "Syncthing", "System", "Continuous file synchronization", "https://syncthing.davidhormigafonso.work", false,
            "syncthing", ["syncthing"]),
        App("bazarr", "Bazarr", "Media", "Subtitle automation", "https://bazarr.davidhormigafonso.work", false,
            "bazarr", ["bazarr"]),
        App("jackett", "Jackett", "Media", "Indexer proxy", "http://192.168.0.32:9117", false,
            "jackett", ["jackett"]),
        App("prowlarr", "Prowlarr", "Media", "Indexer management", "https://prowlarr.davidhormigafonso.work", false,
            "prowlarr", ["prowlarr"]),
        App("cloudflared", "Cloudflared", "Network", "Cloudflare tunnel agent (no web UI)", null, false,
            "cloudflared", ["cloudflared"], "http://cloudflared:2000/ready"),
        App("flaresolverr", "FlareSolverr", "Network", "Proxy challenge solver", "http://192.168.0.32:8191", false,
            "flaresolverr", ["flaresolverr"]),
        App("homeassistant", "Home Assistant", "Home", "Home automation", "https://homeassistant.davidhormigafonso.work", true,
            "big-bear-home-assistant", ["home-assistant"]),
        App("obsidian", "Obsidian", "Knowledge", "Obsidian vault workspace", "https://obsidian.davidhormigafonso.work", true,
            "obsidian", ["obsidian"]),
    ];

    public async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        var existingItems = await db.AppLauncherItems.ToListAsync(cancellationToken);
        var existingById = new Dictionary<string, AppLauncherItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in existingItems.GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = group.FirstOrDefault(item => item.AppId == item.AppId.ToLowerInvariant())
                ?? group.OrderBy(item => item.CreatedAt).First();
            existingById[canonical.AppId] = canonical;
            foreach (var duplicate in group.Where(item => item.Id != canonical.Id))
                duplicate.Enabled = false;
            if (group.Count() > 1)
                logger.LogWarning("Disabled duplicate app catalog IDs matching {AppId}.", canonical.AppId);
        }
        var imported = await loader.LoadAsync(cancellationToken);
        var importedById = imported
            .GroupBy(item => item.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var newlyCreatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in Catalog)
        {
            importedById.TryGetValue(seed.Id, out var configured);
            if (!existingById.TryGetValue(seed.Id, out var item))
            {
                item = new AppLauncherItem
                {
                    AppId = seed.Id,
                    Name = seed.Name,
                    Category = seed.Category,
                    Description = seed.Description,
                    IconUrl = seed.Id == "household" ? "/household-mark.svg" : null,
                    InternalUrl = NormalizeConfiguredUrl(configured?.InternalUrl),
                    OpenUrl = seed.OpenUrl,
                    ExternalUrl = seed.OpenUrl,
                    Favorite = seed.Favorite,
                    Enabled = true,
                    AdminActionsEnabled = seed.CanUpdate,
                };
                db.AppLauncherItems.Add(item);
                existingById[seed.Id] = item;
                newlyCreatedIds.Add(seed.Id);
            }
            else if (IsPlaceholderUrl(item.OpenUrl) && seed.OpenUrl is not null)
            {
                item.OpenUrl = seed.OpenUrl;
                item.ExternalUrl = seed.OpenUrl;
            }

            if (configured is not null)
                item.InternalUrl = NormalizeConfiguredUrl(configured.InternalUrl);
        }

        foreach (var source in imported)
        {
            var id = NormalizeId(source.Id);
            if (id == "jellyseerr") continue;
            if (id is null) continue;
            if (existingById.TryGetValue(id, out var existing))
            {
                if (newlyCreatedIds.Contains(id))
                {
                    existing.Name = source.Name.Trim();
                    existing.Category = string.IsNullOrWhiteSpace(source.Category) ? existing.Category : source.Category.Trim();
                    existing.Description = TrimToNull(source.Description) ?? existing.Description;
                    existing.IconUrl = AppCatalogService.NormalizeBrowserUrl(source.IconUrl, true) ?? existing.IconUrl;
                    existing.Favorite = source.Favorite;
                }
                continue;
            }
            var item = new AppLauncherItem
            {
                AppId = id,
                Name = source.Name.Trim(),
                Category = string.IsNullOrWhiteSpace(source.Category) ? "Other" : source.Category.Trim(),
                Description = TrimToNull(source.Description),
                IconUrl = AppCatalogService.NormalizeBrowserUrl(source.IconUrl, true),
                InternalUrl = NormalizeConfiguredUrl(source.InternalUrl),
                OpenUrl = AppCatalogService.NormalizeBrowserUrl(source.OpenUrl, false),
                ExternalUrl = AppCatalogService.NormalizeBrowserUrl(source.OpenUrl, false),
                Favorite = source.Favorite,
                Enabled = true,
            };
            db.AppLauncherItems.Add(item);
            existingById[id] = item;
        }

        var policies = await db.AllowedComposeApps.ToListAsync(cancellationToken);
        var policiesById = new Dictionary<string, AllowedComposeApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in policies.GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase))
        {
            var canonical = group.FirstOrDefault(item => item.AppId == item.AppId.ToLowerInvariant())
                ?? group.OrderBy(item => item.CreatedAt).First();
            policiesById[canonical.AppId] = canonical;
            db.AllowedComposeApps.RemoveRange(group.Where(item => item.Id != canonical.Id));
        }
        foreach (var seed in Catalog.Where(item => item.MonitoringEnabled))
        {
            importedById.TryGetValue(seed.Id, out var configured);
            var healthCheckUrl = configured is null
                ? seed.HealthCheckUrl
                : NormalizeConfiguredUrl(configured.HealthCheckUrl);
            if (policiesById.TryGetValue(seed.Id, out var existingPolicy))
            {
                existingPolicy.DisplayName = seed.Name;
                existingPolicy.ComposePath = seed.ProjectName!;
                existingPolicy.ProjectName = seed.ProjectName;
                existingPolicy.ContainerNamesJson = JsonSerializer.Serialize(seed.ContainerNames);
                existingPolicy.HealthCheckUrl = healthCheckUrl;
                existingPolicy.HealthCheckTimeoutSeconds = 5;
                existingPolicy.AllowedActionsJson = JsonSerializer.Serialize(
                    seed.CanUpdate ? new[] { "monitor", "update" } : new[] { "monitor" });
                existingPolicy.AdminActionsEnabled = seed.CanUpdate;
                continue;
            }
            var policy = new AllowedComposeApp
            {
                AppId = seed.Id,
                DisplayName = seed.Name,
                ComposePath = seed.ProjectName!,
                ProjectName = seed.ProjectName,
                ContainerNamesJson = JsonSerializer.Serialize(seed.ContainerNames),
                AllowedActionsJson = JsonSerializer.Serialize(seed.CanUpdate ? new[] { "monitor", "update" } : new[] { "monitor" }),
                HealthCheckUrl = healthCheckUrl,
                HealthCheckTimeoutSeconds = 5,
                AdminActionsEnabled = seed.CanUpdate,
            };
            db.AllowedComposeApps.Add(policy);
            policiesById[seed.Id] = policy;
        }

        await MigrateLegacyFavoriteIdsAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("App catalog bootstrap completed with {CatalogCount} canonical entries.", Catalog.Length);
    }

    private async Task MigrateLegacyFavoriteIdsAsync(CancellationToken cancellationToken)
    {
        var legacy = await db.UserAppFavorites
            .Where(item => item.AppId == "jellyseerr")
            .ToListAsync(cancellationToken);
        foreach (var favorite in legacy)
        {
            var current = await db.UserAppFavorites.SingleOrDefaultAsync(
                item => item.UserId == favorite.UserId && item.AppId == "seerr",
                cancellationToken);
            if (current is null)
                favorite.AppId = "seerr";
            else
                db.UserAppFavorites.Remove(favorite);
        }
    }

    private static CatalogSeed App(
        string id,
        string name,
        string category,
        string description,
        string? openUrl,
        bool favorite,
        string projectName,
        string[] containers,
        string? healthCheckUrl = null) =>
        new(id, name, category, description, openUrl, favorite, true, true, projectName, containers, healthCheckUrl ?? openUrl);

    private static CatalogSeed Monitor(
        string id,
        string name,
        string category,
        string description,
        string? openUrl,
        bool favorite,
        string projectName,
        string[] containers) =>
        new(id, name, category, description, openUrl, favorite, true, false, projectName, containers, openUrl);

    private static CatalogSeed Link(
        string id,
        string name,
        string category,
        string description,
        string? openUrl,
        bool favorite) =>
        new(id, name, category, description, openUrl, favorite, false, false, null, [], null);

    private static bool IsPlaceholderUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Host.Equals("example.local", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".example.local", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".example", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeId(string? value)
    {
        var id = value?.Trim().ToLowerInvariant();
        return id is { Length: > 0 and <= 120 }
            && id.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
                ? id
                : null;
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeConfiguredUrl(string? value) =>
        AppCatalogService.NormalizeBrowserUrl(value, false);

    private sealed record CatalogSeed(
        string Id,
        string Name,
        string Category,
        string Description,
        string? OpenUrl,
        bool Favorite,
        bool MonitoringEnabled,
        bool CanUpdate,
        string? ProjectName,
        string[] ContainerNames,
        string? HealthCheckUrl);
}
