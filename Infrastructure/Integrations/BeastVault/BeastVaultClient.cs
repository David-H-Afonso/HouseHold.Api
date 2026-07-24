using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.DTOs;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.BeastVault;

public sealed class BeastVaultClient : HouseholdProviderClientBase, IBeastVaultClient
{
    private readonly HouseholdConnectionSettings _settings;

    public BeastVaultClient(
        HttpClient httpClient,
        IHouseholdProviderAccessService connectionAccess,
        IOptions<HouseholdConnectionSettings> settings
    ) : base(httpClient, connectionAccess, "beast-vault", "Beast Vault")
    {
        _settings = settings.Value;
    }

    public async Task<PokemonModuleListDto> GetPokemonAsync(
        Guid userId,
        string? search,
        IReadOnlyList<int> tagIds,
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
                BuildPublicUrl(_settings.BeastVaultOpenUrl, item.SpriteUrl),
                BuildFallbackSpriteUrl(item.SpeciesId, item.IsShiny),
                item.Tags.Select(tag => new PokemonTagDto(
                    tag.Id,
                    tag.Name,
                    tag.ColorHex,
                    BuildPublicUrl(_settings.BeastVaultOpenUrl, tag.ImagePath)
                )).ToList(),
                BuildPublicUrl(_settings.BeastVaultOpenUrl, $"/pokemon/{item.Id}")
            )).ToList(),
            source.Total,
            Math.Max(skip, 0),
            Math.Clamp(take, 1, 100)
        );
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
            BuildPublicUrl(_settings.BeastVaultOpenUrl, tag.ImagePath)
        )).ToList();
    }

    private static string BuildFallbackSpriteUrl(int speciesId, bool isShiny) =>
        $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{(isShiny ? "shiny/" : string.Empty)}{speciesId}.png";

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
