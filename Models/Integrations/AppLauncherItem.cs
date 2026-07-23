namespace Household.Api.Models.Integrations;

public class AppLauncherItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AppId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string? InternalUrl { get; set; }
    public string? ExternalUrl { get; set; }
    public string? OpenUrl { get; set; }
    public bool Favorite { get; set; }
    public bool AdminActionsEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
