using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IBeastVaultClient
{
    Task<PokemonModuleListDto> GetPokemonAsync(
        Guid userId,
        string? search,
        IReadOnlyList<int> tagIds,
        int skip,
        int take,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<PokemonTagFilterDto>> GetTagsAsync(Guid userId, CancellationToken cancellationToken);
}
