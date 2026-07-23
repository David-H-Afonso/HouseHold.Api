namespace Household.Api.Models.Integrations;

public class IntegrationSecret
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntegrationId { get; set; }
    public string SecretKey { get; set; } = string.Empty;
    public string ProtectedValue { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Integration Integration { get; set; } = null!;
}
