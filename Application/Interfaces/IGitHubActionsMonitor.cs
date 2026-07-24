using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IGitHubActionsMonitor
{
    Task<GitHubActionsConfigDto> GetConfigAsync(CancellationToken cancellationToken);
    Task<GitHubActionsConfigDto> UpdateConfigAsync(UpdateGitHubActionsConfigRequest request, CancellationToken cancellationToken);
    Task<GitHubActionsMonitorDto> GetForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task PollAsync(CancellationToken cancellationToken);
}
