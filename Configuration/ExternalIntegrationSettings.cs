namespace Household.Api.Configuration;

public sealed class ExternalIntegrationSettings
{
    public const string SectionName = "ExternalIntegrationSettings";

    public int GitHubPollSeconds { get; set; } = 60;
    public int GitHubConcurrency { get; set; } = 4;
    public int ProviderAssetMaxBytes { get; set; } = 8 * 1024 * 1024;
    public string WarcraftStatusPathTemplate { get; set; } = "/api/integrations/household/v1/trackings/{id}/status";
    public string PokemonDownloadPathTemplate { get; set; } = "/api/integrations/household/v1/pokemon/{id}/download";
}
