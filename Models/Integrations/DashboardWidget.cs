namespace Household.Api.Models.Integrations;

public class DashboardWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string WidgetType { get; set; } = string.Empty;
    public Guid? IntegrationId { get; set; }
    public int Position { get; set; }
    public bool Enabled { get; set; } = true;
    public string Size { get; set; } = "medium";
    public int SchemaVersion { get; set; } = 1;
    public string? SettingsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Integration? Integration { get; set; }
    public Auth.User User { get; set; } = null!;
}
