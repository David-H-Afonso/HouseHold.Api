using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IJellywatchClient
{
    Task<JellywatchDashboardDto> GetDashboardAsync(Guid userId, CancellationToken cancellationToken);
}
