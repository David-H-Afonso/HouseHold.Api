namespace Household.Api.Models.Integrations;

public class Integration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public IntegrationType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? OpenUrl { get; set; }
    public bool Enabled { get; set; } = true;
    public IntegrationHealthStatus LastHealthStatus { get; set; } = IntegrationHealthStatus.Unknown;
    public DateTime? LastCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<IntegrationSecret> Secrets { get; set; } = new List<IntegrationSecret>();
    public ICollection<DashboardWidget> DashboardWidgets { get; set; } = new List<DashboardWidget>();
    public ICollection<IntegrationActionLog> ActionLogs { get; set; } = new List<IntegrationActionLog>();
}
