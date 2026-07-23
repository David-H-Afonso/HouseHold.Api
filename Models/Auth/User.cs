namespace Household.Api.Models.Auth;

using Household.Api.Models.Integrations;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<HouseholdConsumerConnection> HouseholdConsumerConnections { get; set; } = [];
    public ICollection<HouseholdAuthorizationAttempt> HouseholdAuthorizationAttempts { get; set; } = [];
}
