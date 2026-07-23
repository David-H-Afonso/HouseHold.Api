using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IGamesDatabaseClient
{
    Task<GamesModuleListDto> GetGamesAsync(
        string? search,
        int? statusId,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    );

    Task<GameModuleItemDto?> GetGameAsync(int id, CancellationToken cancellationToken);
    Task<GameModuleItemDto?> UpdateStatusAsync(int id, int statusId, CancellationToken cancellationToken);
    Task<IReadOnlyList<GameStatusOptionDto>> GetStatusesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SteamSearchResultDto>> SearchSteamAsync(string query, CancellationToken cancellationToken);
    Task<object?> AddSteamGameAsync(AddSteamGameRequest request, CancellationToken cancellationToken);
    Task<GamesSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
}
