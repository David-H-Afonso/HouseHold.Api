namespace Household.Api.Models.Integrations;

public enum IntegrationType
{
    CasaOS = 1,
    Docker = 2,
    GamesDatabase = 3,
    Jellywatch = 4,
    Jellyfin = 5,
    Seerr = 6,
    QBittorrent = 7,
    Sonarr = 8,
    Radarr = 9,
    WgEasy = 10,
    WarcraftArchive = 11,
    BeastVault = 12,
}

public enum IntegrationHealthStatus
{
    NotConfigured = 0,
    Unknown = 1,
    Healthy = 2,
    Degraded = 3,
    Offline = 4,
}

public enum IntegrationActionStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}
