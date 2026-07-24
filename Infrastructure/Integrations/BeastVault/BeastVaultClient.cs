using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.DTOs;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.BeastVault;

public sealed class BeastVaultClient : HouseholdProviderClientBase, IBeastVaultClient
{
    private readonly HouseholdConnectionSettings _settings;
    private readonly ExternalIntegrationSettings _externalSettings;

    public BeastVaultClient(
        HttpClient httpClient,
        IHouseholdProviderAccessService connectionAccess,
        IOptions<HouseholdConnectionSettings> settings,
        IOptions<ExternalIntegrationSettings> externalSettings
    ) : base(httpClient, connectionAccess, "beast-vault", "Beast Vault")
    {
        _settings = settings.Value;
        _externalSettings = externalSettings.Value;
    }

    public async Task<PokemonModuleListDto> GetPokemonAsync(
        Guid userId,
        string? search,
        IReadOnlyList<int> tagIds,
        string spriteSource,
        int skip,
        int take,
        CancellationToken cancellationToken
    )
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("search", search),
            new("skip", Math.Max(skip, 0).ToString()),
            new("take", Math.Clamp(take, 1, 100).ToString()),
        };
        values.AddRange(tagIds.Distinct().Take(25).Select(tagId => new KeyValuePair<string, string?>("tagIds", tagId.ToString())));
        var source = await GetRequiredAsync<SourceList>(
            userId,
            "pokemon.read",
            $"/pokemon{BuildQuery(values)}",
            cancellationToken
        );

        return new PokemonModuleListDto(
            source.Items.Select(item => new PokemonModuleItemDto(
                item.Id,
                item.SpeciesId,
                item.SpeciesName,
                item.Nickname,
                item.Level,
                item.IsShiny,
                item.Favorite,
                item.IsEgg,
                item.Type1,
                item.Type2,
                $"/modules/pokemon/sprites/{item.SpeciesId}?shiny={item.IsShiny.ToString().ToLowerInvariant()}&source={Uri.EscapeDataString(spriteSource)}",
                BuildFallbackSpriteUrl(item.SpeciesId, item.IsShiny),
                item.Tags.Select(tag => new PokemonTagDto(
                    tag.Id,
                    tag.Name,
                    tag.ColorHex,
                    BuildTagImageUrl(tag.ImagePath)
                )).ToList(),
                BuildPublicUrl(_settings.BeastVaultOpenUrl, $"/pokemon/{item.Id}")
            )).ToList(),
            source.Total,
            Math.Max(skip, 0),
            Math.Clamp(take, 1, 100)
        );
    }

    public async Task<(byte[] Content, string ContentType)?> GetSpriteAsync(
        Guid userId,
        int speciesId,
        bool shiny,
        string source,
        CancellationToken cancellationToken
    )
    {
        if (speciesId <= 0) return null;
        var path = source switch
        {
            "home" => $"/sprites/pokemon/home/{(shiny ? "shiny/" : string.Empty)}{speciesId}.png",
            "artwork" => $"/sprites/pokemon/artwork/{(shiny ? "shiny/" : string.Empty)}{speciesId}.png",
            "default" => $"/sprites/pokemon/{(shiny ? "shiny/" : string.Empty)}{speciesId}.png",
            "showdown" => $"/sprites/pokemon/showdown/{(shiny ? "shiny/" : string.Empty)}{speciesId}.gif",
            "github" => $"/sprites/pokemon/github/{(shiny ? "shiny/" : string.Empty)}{speciesId}.png",
            _ => throw new ArgumentException("Unsupported Pokemon sprite source."),
        };
        var file = await DownloadAsync(userId, "pokemon.read", path, _externalSettings.ProviderAssetMaxBytes, cancellationToken);
        return file is null || !IsAllowedImageContentType(file.ContentType)
            ? null
            : (file.Content, file.ContentType);
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> DownloadPokemonAsync(
        Guid userId,
        int id,
        CancellationToken cancellationToken
    )
    {
        if (id <= 0) return null;
        var path = BuildConfiguredPath(_externalSettings.PokemonDownloadPathTemplate, id.ToString());
        var file = await DownloadAsync(userId, "pokemon.download", path, _externalSettings.ProviderAssetMaxBytes, cancellationToken);
        if (file is null) return null;
        return (file.Content, "application/octet-stream", SanitizeFileName(file.FileName, id));
    }

    private static string SanitizeFileName(string? fileName, int id)
    {
        var candidate = string.IsNullOrWhiteSpace(fileName) ? $"pokemon-{id}.pk" : Path.GetFileName(fileName);
        var safe = new string(candidate.Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? $"pokemon-{id}.pk" : safe[..Math.Min(safe.Length, 120)];
    }

    private static string BuildConfiguredPath(string template, string id)
    {
        var path = template.Replace("{id}", id, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(path) || path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\') || path.Any(char.IsControl))
            throw new ArgumentException("Pokemon download path template must be a relative provider path.");
        return path;
    }

    public async Task<IReadOnlyList<PokemonTagFilterDto>> GetTagsAsync(
        Guid userId,
        CancellationToken cancellationToken
    )
    {
        var tags = await GetRequiredAsync<List<SourceFilterTag>>(userId, "pokemon.read", "/tags", cancellationToken);
        return tags.Select(tag => new PokemonTagFilterDto(
            tag.Id,
            tag.Name,
            tag.PokemonCount,
            tag.Category,
            tag.ColorHex,
            BuildTagImageUrl(tag.ImagePath)
        )).ToList();
    }

    public async Task<(byte[] Content, string ContentType)?> GetTagImageAsync(
        Guid userId,
        string fileName,
        CancellationToken cancellationToken
    )
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || !string.Equals(safeName, fileName, StringComparison.Ordinal)
            || safeName.Length > 180 || safeName.Any(character => !char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            return null;

        var file = await DownloadAsync(userId, "pokemon.read", $"/tags/images/{Uri.EscapeDataString(safeName)}", _externalSettings.ProviderAssetMaxBytes, cancellationToken);
        return file is null || !IsAllowedImageContentType(file.ContentType) ? null : (file.Content, file.ContentType);
    }

    private string? BuildTagImageUrl(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        var candidate = imagePath.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            var configured = _settings.BeastVaultOpenUrl?.TrimEnd('/');
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
                || !string.Equals(absolute.Host, configuredUri.Host, StringComparison.OrdinalIgnoreCase)
                || absolute.Port != configuredUri.Port)
                return null;
            candidate = absolute.PathAndQuery;
        }

        if (!candidate.StartsWith("/tags/images/", StringComparison.OrdinalIgnoreCase)) return null;
        var fileName = Uri.UnescapeDataString(candidate.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Any(character => !char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            return null;
        return $"/modules/pokemon/tags/images/{Uri.EscapeDataString(fileName)}";
    }

    private static string BuildFallbackSpriteUrl(int speciesId, bool isShiny) =>
        $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{(isShiny ? "shiny/" : string.Empty)}{speciesId}.png";

    private static bool IsAllowedImageContentType(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase);

    private sealed class SourceList
    {
        public List<SourcePokemon> Items { get; set; } = [];
        public int Total { get; set; }
    }

    private sealed class SourcePokemon
    {
        public int Id { get; set; }
        public int SpeciesId { get; set; }
        public string SpeciesName { get; set; } = string.Empty;
        public string? Nickname { get; set; }
        public int Level { get; set; }
        public bool IsShiny { get; set; }
        public bool Favorite { get; set; }
        public bool IsEgg { get; set; }
        public string? Type1 { get; set; }
        public string? Type2 { get; set; }
        public string? SpriteUrl { get; set; }
        public List<SourceTag> Tags { get; set; } = [];
    }

    private class SourceTag
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ImagePath { get; set; }
        public string? ColorHex { get; set; }
    }

    private sealed class SourceFilterTag : SourceTag
    {
        public int PokemonCount { get; set; }
        public string Category { get; set; } = string.Empty;
    }
}
