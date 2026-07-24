using Household.Api.Models.Auth;

namespace Household.Api.Models.Integrations;

public sealed class UserAppFavorite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string AppId { get; set; } = string.Empty;
    public bool Favorite { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
