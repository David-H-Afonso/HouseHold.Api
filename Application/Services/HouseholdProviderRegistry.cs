using Household.Api.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Household.Api.Application.Services;

public sealed record HouseholdProviderDefinition(
    string Id,
    string DisplayName,
    string? BaseUrl,
    string? OpenUrl,
    string ConsentPath,
    IReadOnlyList<string> Scopes,
    bool Configured,
    string? RedirectUri
);

public class HouseholdProviderRegistry
{
    private readonly HouseholdConnectionSettings _settings;
    private readonly string _clientId;
    private readonly IReadOnlyList<ProviderTemplate> _templates;

    public HouseholdProviderRegistry(IOptions<HouseholdConnectionSettings> settings)
    {
        _settings = settings.Value;
        _clientId = string.IsNullOrWhiteSpace(_settings.ClientId) || _settings.ClientId.Length > 100
            ? "household"
            : _settings.ClientId.Trim();
        _templates =
        [
            new("doit", "DoIt", _settings.DoItBaseUrl, _settings.DoItOpenUrl, "/integrations/household/authorize", ["profile.read", "tasks.read", "tasks.complete", "tasks.undo", "tasks.create", "calendar.read"]),
            new("games-database", "Games Database", _settings.GamesDatabaseBaseUrl, _settings.GamesDatabaseOpenUrl, "/#/integrations/household/authorize", ["profile.read", "games.read", "games.status.write"]),
            new("jellywatch", "Jellywatch", _settings.JellywatchBaseUrl, _settings.JellywatchOpenUrl, "/#/integrations/household/authorize", ["profile.read", "activity.read", "upcoming.read", "media.state.write", "media.rating.write"]),
            new("beast-vault", "Beast Vault", _settings.BeastVaultBaseUrl, _settings.BeastVaultOpenUrl, "/integrations/household/authorize", ["profile.read", "pokemon.read", "pokemon.favorite.write", "pokemon.notes.write", "pokemon.download"]),
            new("warcraft-archive", "Warcraft Archive", _settings.WarcraftArchiveBaseUrl, _settings.WarcraftArchiveOpenUrl, "/#/integrations/household/authorize", ["profile.read", "dashboard.read", "tracking.status.write"]),
        ];
    }

    public IReadOnlyList<HouseholdProviderDefinition> GetAll() => _templates.Select(CreateDefinition).ToList();

    public bool TryGet(string provider, out HouseholdProviderDefinition definition)
    {
        var template = _templates.FirstOrDefault(item => string.Equals(item.Id, provider, StringComparison.Ordinal));
        if (template is null)
        {
            definition = null!;
            return false;
        }

        definition = CreateDefinition(template);
        return true;
    }

    public string? BuildAuthorizationUrl(
        HouseholdProviderDefinition provider,
        string state,
        string challenge
    )
    {
        if (!provider.Configured || provider.OpenUrl is null || provider.RedirectUri is null)
            return null;

        var consentUrl = $"{provider.OpenUrl.TrimEnd('/')}{provider.ConsentPath}";
        var query = QueryString.Create(
            new Dictionary<string, string?>
            {
                ["client_id"] = _clientId,
                ["redirect_uri"] = provider.RedirectUri,
                ["state"] = state,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["scope"] = string.Join(' ', provider.Scopes),
            }
        );
        return $"{consentUrl}{query}";
    }

    public string? BuildCompletionUrl(string provider, bool success, string? reason = null)
    {
        if (!TryGetAbsoluteHttpUrl(_settings.PublicUrl, out var publicUrl))
            return null;

        return QueryHelpers.AddQueryString(
            $"{publicUrl}/settings/integrations",
            new Dictionary<string, string?>
            {
                ["provider"] = provider,
                ["connection"] = success ? "connected" : "error",
                ["reason"] = success ? null : reason ?? "authorization_failed",
            }
        );
    }

    public string ClientId => _clientId;

    private HouseholdProviderDefinition CreateDefinition(ProviderTemplate template)
    {
        var hasBaseUrl = TryGetAbsoluteHttpUrl(template.BaseUrl, out var baseUrl);
        var hasOpenUrl = TryGetAbsoluteHttpUrl(template.OpenUrl, out var openUrl);
        var hasApiPublicUrl = TryGetAbsoluteHttpUrl(_settings.ApiPublicUrl, out var apiPublicUrl);
        var redirectUri = hasApiPublicUrl
            ? $"{apiPublicUrl}/integrations/callback/{template.Id}"
            : null;
        var configured = hasBaseUrl && hasOpenUrl && redirectUri is not null && TryGetAbsoluteHttpUrl(redirectUri, out _);

        return new HouseholdProviderDefinition(
            template.Id,
            template.DisplayName,
            hasBaseUrl ? baseUrl : null,
            hasOpenUrl ? openUrl : null,
            template.ConsentPath,
            template.Scopes,
            configured,
            redirectUri
        );
    }

    private static bool TryGetAbsoluteHttpUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        var candidate = value?.Trim().TrimEnd('/');
        if (
            string.IsNullOrWhiteSpace(candidate)
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
        )
            return false;

        normalized = candidate;
        return true;
    }

    private sealed record ProviderTemplate(
        string Id,
        string DisplayName,
        string? BaseUrl,
        string? OpenUrl,
        string ConsentPath,
        IReadOnlyList<string> Scopes
    );
}
