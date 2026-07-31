using Household.Api.Application.Interfaces;

namespace Household.Api.Application.Services;

public static class PokemonSpriteSources
{
    public const string Home = "home";
    public const string Artwork = "artwork";
    public const string Default = "default";
    public const string Showdown = "showdown";
    public const string GitHub = "github";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        Home,
        Artwork,
        Default,
        Showdown,
        GitHub,
    };

    public static bool IsAllowed(string? source) => source is not null && Allowed.Contains(source);

    public static async Task<string?> ResolveAsync(
        string? requestedSource,
        Guid userId,
        IUserSettingsService settings,
        CancellationToken cancellationToken
    )
    {
        if (requestedSource is not null)
            return IsAllowed(requestedSource) ? requestedSource : null;

        var persistedSource = (await settings.GetPreferencesAsync(userId, cancellationToken)).PokemonSpriteSource;
        return IsAllowed(persistedSource) ? persistedSource : null;
    }
}
