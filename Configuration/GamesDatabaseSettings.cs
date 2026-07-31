namespace Household.Api.Configuration;

public class GamesDatabaseSettings
{
    public const string SectionName = "GamesDatabaseSettings";

    public int TimeoutSeconds { get; set; } = 15;
}
