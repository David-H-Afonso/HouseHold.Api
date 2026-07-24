using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IWarcraftArchiveClient
{
    Task<WarcraftQuickStatusDto> GetQuickStatusAsync(Guid userId, CancellationToken cancellationToken);
    Task<WarcraftWeeklyDto> GetWeeklyAsync(Guid userId, CancellationToken cancellationToken);
}
