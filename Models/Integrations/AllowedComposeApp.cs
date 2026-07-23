namespace Household.Api.Models.Integrations;

public class AllowedComposeApp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ComposePath { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ContainerNamesJson { get; set; }
    public string? AllowedActionsJson { get; set; }
    public string? HealthCheckUrl { get; set; }
    public int HealthCheckTimeoutSeconds { get; set; } = 10;
    public bool AdminActionsEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
