using Household.Api.Application.Interfaces;
using Household.Api.DTOs;

namespace Household.Api.Application.Services;

public class ContainerStatusService : IContainerStatusService
{
    private readonly IAppLauncherConfigLoader _loader;
    private readonly IDockerClient _dockerClient;

    public ContainerStatusService(IAppLauncherConfigLoader loader, IDockerClient dockerClient)
    {
        _loader = loader;
        _dockerClient = dockerClient;
    }

    public async Task<IReadOnlyList<ContainerStatusDto>> GetAppContainersAsync(
        string appId,
        CancellationToken cancellationToken
    )
    {
        var config = await _loader.LoadAsync(cancellationToken);
        var app = config.FirstOrDefault(item => string.Equals(item.Id, appId, StringComparison.OrdinalIgnoreCase));
        if (app is null)
            return [];

        return await _dockerClient.InspectContainersAsync(app.ContainerNames.ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<AppContainerStatusDto>> GetAllAppStatusesAsync(CancellationToken cancellationToken)
    {
        var config = await _loader.LoadAsync(cancellationToken);
        var results = new List<AppContainerStatusDto>();

        foreach (var app in config)
        {
            var containers = await _dockerClient.InspectContainersAsync(app.ContainerNames.ToList(), cancellationToken);
            results.Add(new AppContainerStatusDto(app.Id, containers));
        }

        return results;
    }
}
