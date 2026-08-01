namespace Household.Api.Configuration;

public class SeerrSettings
{
    public const string SectionName = "SeerrSettings";

    /// <summary>Optional one-time bootstrap internal URL. Admin config in DB takes precedence.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional bootstrap public browser URL used for deep links.</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Optional one-time bootstrap API key. It is persisted encrypted and never returned.</summary>
    public string? ApiKey { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaxJsonBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Seconds a resolved Household user -> Seerr user mapping is cached in memory.</summary>
    public int UserMappingCacheSeconds { get; set; } = 900;
}
