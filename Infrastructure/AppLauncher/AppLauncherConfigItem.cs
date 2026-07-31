namespace Household.Api.Infrastructure.AppLauncher;

public class AppLauncherConfigItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? InternalUrl { get; set; }
    public string? OpenUrl { get; set; }
    public bool Favorite { get; set; }
    public string? HealthCheckUrl { get; set; }
    public IReadOnlyList<string> ContainerNames { get; set; } = [];
    public string? ComposePath { get; set; }
    public bool AdminActionsEnabled { get; set; }
}
