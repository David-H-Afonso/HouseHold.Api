namespace Household.Api.Configuration;

public class GamesDatabaseSettings
{
    public const string SectionName = "GamesDatabaseSettings";

    public string? BaseUrl { get; set; }
    public string? OpenUrl { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
}
