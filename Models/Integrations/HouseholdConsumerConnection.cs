using Household.Api.Models.Auth;

namespace Household.Api.Models.Integrations;

public enum HouseholdConnectionStatus
{
    Disconnected,
    Connected,
    Expired,
    Error,
}

public class HouseholdConsumerConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public string ProtectedRefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAt { get; set; }
    public string SourceConnectionId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string AccountDisplayName { get; set; } = string.Empty;
    public string GrantedScopes { get; set; } = string.Empty;
    public HouseholdConnectionStatus Status { get; set; } = HouseholdConnectionStatus.Connected;
    public string? LastError { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
