using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IDockerClient
{
    Task<IReadOnlyList<ContainerStatusDto>> InspectContainersAsync(
        IReadOnlyList<string> containerNames,
        CancellationToken cancellationToken
    );
}
