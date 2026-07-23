using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IGamesDatabaseClient
{
    Task<GamesModuleListDto> GetGamesAsync(
        Guid userId,
        string? search,
        int? statusId,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    );

    Task<GameModuleItemDto?> GetGameAsync(Guid userId, int id, CancellationToken cancellationToken);
    Task<GameModuleItemDto?> UpdateStatusAsync(Guid userId, int id, int statusId, CancellationToken cancellationToken);
    Task<IReadOnlyList<GameStatusOptionDto>> GetStatusesAsync(Guid userId, CancellationToken cancellationToken);
    Task<GamesSummaryDto> GetSummaryAsync(Guid userId, CancellationToken cancellationToken);
}
