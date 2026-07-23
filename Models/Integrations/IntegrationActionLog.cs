namespace Household.Api.Models.Integrations;

public class IntegrationActionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public Guid? IntegrationId { get; set; }
    public string? AppId { get; set; }
    public string Action { get; set; } = string.Empty;
    public IntegrationActionStatus Status { get; set; } = IntegrationActionStatus.Queued;
    public string Source { get; set; } = "Household";
    public string? RequestSummaryJson { get; set; }
    public string? ResultSummaryJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }

    public Integration? Integration { get; set; }
}
