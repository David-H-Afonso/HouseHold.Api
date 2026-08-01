using Household.Api.Models.Integrations;

namespace Household.Api.Models.Auth;

public sealed class UserPreference
{
    public Guid UserId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string? TimeZoneId { get; set; }
    public string VisualPreference { get; set; } = "system";
    public string PokemonSpriteSource { get; set; } = "home";
    public string GamesStatusOrderJson { get; set; } = "[]";
    public string HiddenGitHubReposJson { get; set; } = "[]";
    public string? JellyfinUserId { get; set; }
    public bool SeerrJellyfinMappingApproved { get; set; }
    public int? SeerrUserIdOverride { get; set; }
    public int? SeerrResolvedUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}

public sealed class UserInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? RedeemedUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User CreatedByUser { get; set; } = null!;
    public User? RedeemedUser { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorUserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? SummaryJson { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? ActorUser { get; set; }
}
