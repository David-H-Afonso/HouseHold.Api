using Household.Api.Models.Auth;

namespace Household.Api.Models.Integrations;

public class HouseholdAuthorizationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string StateHash { get; set; } = string.Empty;
    public string ProtectedCodeVerifier { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string RequestedScopes { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
