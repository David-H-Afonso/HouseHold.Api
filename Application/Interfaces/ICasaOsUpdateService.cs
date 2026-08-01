using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface ICasaOsUpdateService
{
    Task<CasaOsUpdateConfigDto> GetConfigAsync(CancellationToken cancellationToken);
    Task<CasaOsUpdateConfigDto> UpdateConfigAsync(
        UpdateCasaOsUpdateConfigRequest request,
        CancellationToken cancellationToken
    );
    Task<bool> RefreshTokenAsync(CancellationToken cancellationToken);
    Task<CasaOsAppCapabilities> GetAppCapabilitiesAsync(CancellationToken cancellationToken);
    Task<CasaOsQueuedOperationDto> QueueUpdateAsync(
        Guid actorUserId,
        string appId,
        string confirmation,
        CancellationToken cancellationToken
    );
    Task<CasaOsQueuedOperationDto> QueueRollbackAsync(
        Guid actorUserId,
        string appId,
        string confirmation,
        string? backupId,
        CancellationToken cancellationToken
    );
    Task<IReadOnlyList<CasaOsActionStatusDto>> GetHistoryAsync(
        string appId,
        CancellationToken cancellationToken
    );
    Task<CasaOsActionStatusDto?> GetStatusAsync(
        string appId,
        Guid actionLogId,
        CancellationToken cancellationToken
    );
}

public static class CasaOsUpdatePolicy
{
    public const string IntegrationName = "CasaOS Update Operations";
    public const string TokenSecretKey = "raw-jwt";
    public const string RefreshTokenSecretKey = "refresh-jwt";
    public const string UpdateAction = "casaos.compose.update";
    public const string RollbackAction = "casaos.compose.rollback";

    private static readonly IReadOnlyDictionary<string, string> ProjectNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["household"] = "household",
            ["doit"] = "doit",
            ["gamesdatabase"] = "gamesdatabase",
            ["jellywatch"] = "jellywatch",
            ["beastvault"] = "beastvault",
            ["warcraftarchive"] = "warcraftarchive",
            ["portafolio"] = "portafolio",
            ["jellyfin"] = "jellyfin",
            ["seerr"] = "big-bear-seerr",
            ["qbittorrent"] = "qbittorrent",
            ["sonarr"] = "sonarr",
            ["radarr"] = "radarr",
            ["komga"] = "komga",
            ["wg-easy"] = "wg-easy",
            ["audiobookshelf"] = "big-bear-audiobookshelf",
            ["syncthing"] = "syncthing",
            ["bazarr"] = "bazarr",
            ["jackett"] = "jackett",
            ["prowlarr"] = "prowlarr",
            ["cloudflared"] = "cloudflared",
            ["flaresolverr"] = "flaresolverr",
            ["homeassistant"] = "big-bear-home-assistant",
            ["obsidian"] = "obsidian",
        };

    public static IReadOnlyCollection<string> AppIds { get; } =
        Array.AsReadOnly(ProjectNames.Keys.Order().ToArray());

    public static bool IsAllowedAppId(string? appId) =>
        appId is not null && ProjectNames.ContainsKey(appId);

    public static string GetProjectName(string appId) =>
        ProjectNames.TryGetValue(appId, out var projectName)
            ? projectName
            : throw new KeyNotFoundException("CasaOS app is not allowlisted.");

    public static bool TryGetAppId(string projectName, out string? appId)
    {
        appId = ProjectNames.FirstOrDefault(item => item.Value == projectName).Key;
        return appId is not null;
    }

    public static bool IsReservedIntegration(Models.Integrations.IntegrationType type, string? name) =>
        type == Models.Integrations.IntegrationType.CasaOS
        && string.Equals(name?.Trim(), IntegrationName, StringComparison.OrdinalIgnoreCase);
}
