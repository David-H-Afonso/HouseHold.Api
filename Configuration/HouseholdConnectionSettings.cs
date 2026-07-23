namespace Household.Api.Configuration;

public class HouseholdConnectionSettings
{
    public const string SectionName = "HouseholdConnectionSettings";

    public string? PublicUrl { get; set; }
    public string? ApiPublicUrl { get; set; }
    public string ClientId { get; set; } = "household";
    public string DataProtectionKeysPath { get; set; } = "/data/keys";
    public string? DoItBaseUrl { get; set; }
    public string? DoItOpenUrl { get; set; }
    public string? GamesDatabaseBaseUrl { get; set; }
    public string? GamesDatabaseOpenUrl { get; set; }
    public string? JellywatchBaseUrl { get; set; }
    public string? JellywatchOpenUrl { get; set; }
    public string? BeastVaultBaseUrl { get; set; }
    public string? BeastVaultOpenUrl { get; set; }
    public string? WarcraftArchiveBaseUrl { get; set; }
    public string? WarcraftArchiveOpenUrl { get; set; }
}
