using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IBeastVaultClient
{
    Task<PokemonModuleListDto> GetPokemonAsync(
        Guid userId,
        string? search,
        IReadOnlyList<int> tagIds,
        string spriteSource,
        int skip,
        int take,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<PokemonTagFilterDto>> GetTagsAsync(Guid userId, CancellationToken cancellationToken);
    Task<(byte[] Content, string ContentType)?> GetTagImageAsync(Guid userId, string fileName, CancellationToken cancellationToken);
    Task<(byte[] Content, string ContentType)?> GetSpriteAsync(Guid userId, int speciesId, bool shiny, string source, CancellationToken cancellationToken);
    Task<(byte[] Content, string ContentType, string FileName)?> DownloadPokemonAsync(Guid userId, int id, CancellationToken cancellationToken);
}
