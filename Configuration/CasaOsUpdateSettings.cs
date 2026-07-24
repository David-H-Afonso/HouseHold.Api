namespace Household.Api.Configuration;

public sealed class CasaOsUpdateSettings
{
    public const string SectionName = "CasaOsUpdateSettings";

    public string BackupRoot { get; set; } = "/data/compose-backups";
    public int RequestTimeoutSeconds { get; set; } = 15;
    public int MaxYamlBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxJsonBytes { get; set; } = 256 * 1024;
}
