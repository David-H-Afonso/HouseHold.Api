namespace Household.Api.Configuration;

public class SeedSettings
{
    public const string SectionName = "SeedSettings";

    public bool AdminEnabled { get; set; } = false;
    public string AdminEmail { get; set; } = "admin@local";
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "Admin123!123!";
    public bool DemoDataEnabled { get; set; } = false;
}
