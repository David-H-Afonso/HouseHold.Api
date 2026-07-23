using Household.Api.Infrastructure.AppLauncher;

namespace Household.Api.Application.Interfaces;

public interface IAppLauncherConfigLoader
{
    Task<IReadOnlyList<AppLauncherConfigItem>> LoadAsync(CancellationToken cancellationToken);
}
