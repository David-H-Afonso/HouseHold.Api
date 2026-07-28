using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface ICasaOsUpdateService
{
    Task<CasaOsUpdateConfigDto> GetConfigAsync(CancellationToken cancellationToken);
    Task<CasaOsUpdateConfigDto> UpdateConfigAsync(
        UpdateCasaOsUpdateConfigRequest request,
        CancellationToken cancellationToken
    );
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

    private static readonly HashSet<string> AllowedApps =
    [
        "household",
        "doit",
        "gamesdatabase",
        "jellywatch",
        "beastvault",
        "warcraftarchive",
        "jellyfin",
    ];

    public static IReadOnlyCollection<string> AppIds { get; } = Array.AsReadOnly(AllowedApps.Order().ToArray());

    public static bool IsAllowedAppId(string? appId) => appId is not null && AllowedApps.Contains(appId);

    public static bool IsReservedIntegration(Models.Integrations.IntegrationType type, string? name) =>
        type == Models.Integrations.IntegrationType.CasaOS
        && string.Equals(name?.Trim(), IntegrationName, StringComparison.OrdinalIgnoreCase);
}
