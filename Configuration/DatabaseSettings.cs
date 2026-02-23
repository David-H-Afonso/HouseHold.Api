namespace Household.Api.Configuration;

public class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";

    public string DatabasePath { get; set; } = "/data/household.db";
    public bool EnableSensitiveDataLogging { get; set; } = false;
}
