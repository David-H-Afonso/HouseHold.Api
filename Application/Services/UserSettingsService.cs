using System.Text.Json;
using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Auth;
using Household.Api.Models.Integrations;
using Microsoft.EntityFrameworkCore;
using Household.Api.Infrastructure.Integrations.GitHub;

namespace Household.Api.Application.Services;

public sealed class UserSettingsService(AppDbContext db) : IUserSettingsService
{
    public const int CurrentSchemaVersion = 1;
    private static readonly HashSet<string> VisualPreferences = new(StringComparer.Ordinal) { "system", "light", "dark" };
    private static readonly IReadOnlyList<DashboardWidgetCatalogItemDto> Catalog =
    [
        new("apps", "Applications", "small", ["small", "medium", "large"], true),
        new("games", "Games", "medium", ["medium", "large"], true),
        new("doit", "Today", "medium", ["small", "medium", "large"], true),
        new("calendar", "Calendar", "medium", ["medium", "large"], false),
        new("jellywatch", "Jellywatch", "medium", ["medium", "large"], true),
        new("warcraft", "Warcraft", "medium", ["small", "medium", "large"], true),
        new("pokemon", "Pokemon", "medium", ["medium", "large"], false),
        new("jellyfin", "Jellyfin", "medium", ["medium", "large"], true),
        new("github-actions", "GitHub Actions", "medium", ["small", "medium", "large"], false),
    ];

    public async Task<UserPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        return ToDto(preference);
    }

    public async Task<UserPreferencesDto> UpdatePreferencesAsync(
        Guid userId,
        UpdateUserPreferencesRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.SchemaVersion != CurrentSchemaVersion)
            throw new ArgumentException("Unsupported preference schema version.");

        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        if (request.TimeZoneId is not null)
            preference.TimeZoneId = NormalizeIanaTimeZone(request.TimeZoneId);
        if (request.VisualPreference is not null)
        {
            if (!VisualPreferences.Contains(request.VisualPreference))
                throw new ArgumentException("Unsupported visual preference.");
            preference.VisualPreference = request.VisualPreference;
        }
        if (request.PokemonSpriteSource is not null)
        {
            if (!PokemonSpriteSources.IsAllowed(request.PokemonSpriteSource))
                throw new ArgumentException("Unsupported Pokemon sprite source.");
            preference.PokemonSpriteSource = request.PokemonSpriteSource;
        }
        if (request.GamesStatusOrder is not null)
        {
            if (request.GamesStatusOrder.Count > 50 || request.GamesStatusOrder.Any(id => id <= 0))
                throw new ArgumentException("Games status order is invalid.");
            preference.GamesStatusOrderJson = JsonSerializer.Serialize(request.GamesStatusOrder.Distinct());
        }
        if (request.HiddenGitHubRepos is not null)
        {
            var allowed = GitHubActionsMonitor.Repositories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (request.HiddenGitHubRepos.Any(repository => !allowed.Contains(repository)))
                throw new ArgumentException("Hidden repository is not in the monitor allowlist.");
            preference.HiddenGitHubReposJson = JsonSerializer.Serialize(
                request.HiddenGitHubRepos.Distinct(StringComparer.OrdinalIgnoreCase)
            );
        }
        if (request.ClearJellyfinUserId)
            preference.JellyfinUserId = null;
        else if (request.JellyfinUserId is not null)
            preference.JellyfinUserId = NormalizeJellyfinUserId(request.JellyfinUserId);

        preference.SchemaVersion = CurrentSchemaVersion;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(preference);
    }

    public IReadOnlyList<DashboardWidgetCatalogItemDto> GetWidgetCatalog() => Catalog;

    public async Task<DashboardLayoutDto> GetLayoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        var widgets = await db.DashboardWidgets.AsNoTracking()
            .Where(widget => widget.UserId == userId)
            .OrderBy(widget => widget.Position)
            .ToListAsync(cancellationToken);
        return widgets.Count == 0 ? await ResetLayoutAsync(userId, cancellationToken) : ToLayout(widgets);
    }

    public async Task<DashboardLayoutDto> UpdateLayoutAsync(
        Guid userId,
        UpdateDashboardLayoutRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.SchemaVersion != CurrentSchemaVersion || request.Widgets.Count != Catalog.Count)
            throw new ArgumentException("Dashboard layout schema is invalid.");
        var catalog = Catalog.ToDictionary(item => item.Type, StringComparer.Ordinal);
        if (request.Widgets.Select(item => item.Type).Distinct(StringComparer.Ordinal).Count() != Catalog.Count)
            throw new ArgumentException("Dashboard widgets must be unique and complete.");
        if (request.Widgets.Select(item => item.Position).Distinct().Count() != Catalog.Count)
            throw new ArgumentException("Dashboard positions must be unique.");
        if (request.Widgets.Any(item => item.Position < 0 || item.Position >= Catalog.Count))
            throw new ArgumentException("Dashboard positions must form a zero-based sequence.");
        foreach (var item in request.Widgets)
        {
            if (!catalog.TryGetValue(item.Type, out var definition) || !definition.AllowedSizes.Contains(item.Size))
                throw new ArgumentException("Dashboard widget type or size is invalid.");
            ValidateSettingsJson(item.SettingsJson);
        }

        var existing = await db.DashboardWidgets.Where(widget => widget.UserId == userId).ToListAsync(cancellationToken);
        db.DashboardWidgets.RemoveRange(existing);
        db.DashboardWidgets.AddRange(request.Widgets.Select(item => new DashboardWidget
        {
            UserId = userId,
            WidgetType = item.Type,
            Position = item.Position,
            Enabled = item.Visible,
            Size = item.Size,
            SettingsJson = item.SettingsJson,
            SchemaVersion = CurrentSchemaVersion,
        }));
        await db.SaveChangesAsync(cancellationToken);
        return await GetLayoutWithoutResetAsync(userId, cancellationToken);
    }

    public async Task<DashboardLayoutDto> ResetLayoutAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await db.DashboardWidgets.Where(widget => widget.UserId == userId).ToListAsync(cancellationToken);
        db.DashboardWidgets.RemoveRange(existing);
        db.DashboardWidgets.AddRange(Catalog.Select((item, position) => new DashboardWidget
        {
            UserId = userId,
            WidgetType = item.Type,
            Position = position,
            Enabled = item.DefaultVisible,
            Size = item.DefaultSize,
            SchemaVersion = CurrentSchemaVersion,
        }));
        await db.SaveChangesAsync(cancellationToken);
        return await GetLayoutWithoutResetAsync(userId, cancellationToken);
    }

    private async Task<UserPreference> GetOrCreatePreferenceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var preference = await db.UserPreferences.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (preference is not null)
            return preference;
        preference = new UserPreference { UserId = userId };
        db.UserPreferences.Add(preference);
        await db.SaveChangesAsync(cancellationToken);
        return preference;
    }

    private async Task<DashboardLayoutDto> GetLayoutWithoutResetAsync(Guid userId, CancellationToken cancellationToken) =>
        ToLayout(await db.DashboardWidgets.AsNoTracking()
            .Where(widget => widget.UserId == userId)
            .OrderBy(widget => widget.Position)
            .ToListAsync(cancellationToken));

    private static DashboardLayoutDto ToLayout(IReadOnlyList<DashboardWidget> widgets) =>
        new(CurrentSchemaVersion, widgets.Select(widget => new DashboardLayoutItemDto(
            widget.WidgetType,
            widget.Position,
            widget.Enabled,
            widget.Size,
            widget.SettingsJson
        )).ToList());

    private static UserPreferencesDto ToDto(UserPreference preference) => new(
        preference.SchemaVersion,
        preference.TimeZoneId,
        preference.VisualPreference,
        preference.PokemonSpriteSource,
        DeserializeList<int>(preference.GamesStatusOrderJson),
        DeserializeList<string>(preference.HiddenGitHubReposJson),
        preference.JellyfinUserId
    );

    private static IReadOnlyList<T> DeserializeList<T>(string json)
    {
        try { return JsonSerializer.Deserialize<List<T>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string NormalizeIanaTimeZone(string value)
    {
        var candidate = value.Trim();
        if (candidate.Length is < 1 or > 128)
            throw new ArgumentException("Time zone is invalid.");
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(candidate);
            if (candidate == "UTC") return candidate;
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var ianaId) && ianaId is not null)
                return ianaId;
            if (candidate.Contains('/')) return candidate;
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }
        throw new ArgumentException("Time zone must be a valid IANA identifier.");
    }

    private static string NormalizeJellyfinUserId(string value)
    {
        var candidate = value.Trim();
        if (candidate.Length is < 1 or > 128 || candidate.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Jellyfin user ID is invalid.");
        return candidate;
    }

    private static void ValidateSettingsJson(string? value)
    {
        if (value is null) return;
        if (value.Length > 4000) throw new ArgumentException("Widget settings are too large.");
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Widget settings must be a JSON object.");
        }
        catch (JsonException)
        {
            throw new ArgumentException("Widget settings must be valid JSON.");
        }
    }
}
