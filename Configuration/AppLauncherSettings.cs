namespace Household.Api.Configuration;

public class AppLauncherSettings
{
    public const string SectionName = "AppLauncherSettings";

    public string ConfigPath { get; set; } = "/data/app-launcher.json";
}
