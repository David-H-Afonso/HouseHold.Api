namespace Household.Api.DTOs;

public record PokemonTagDto(int Id, string Name, string? ColorHex, string? ImageUrl);

public record PokemonTagFilterDto(
    int Id,
    string Name,
    int PokemonCount,
    string Category,
    string? ColorHex,
    string? ImageUrl
);

public record PokemonModuleItemDto(
    int Id,
    int SpeciesId,
    string SpeciesName,
    string? FormName,
    int SpriteId,
    string? Nickname,
    int Level,
    bool IsShiny,
    bool Favorite,
    bool IsEgg,
    string? Type1,
    string? Type2,
    string? SpriteUrl,
    string FallbackSpriteUrl,
    DateTime? AddedAt,
    IReadOnlyList<PokemonTagDto> Tags,
    string? OpenUrl
);

public record PokemonModuleListDto(
    IReadOnlyList<PokemonModuleItemDto> Items,
    int Total,
    int Skip,
    int Take
);
