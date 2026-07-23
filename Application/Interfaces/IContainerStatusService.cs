using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IContainerStatusService
{
    Task<IReadOnlyList<ContainerStatusDto>> GetAppContainersAsync(string appId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AppContainerStatusDto>> GetAllAppStatusesAsync(CancellationToken cancellationToken);
}
