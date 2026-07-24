using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IJellyfinService
{
    Task<JellyfinConfigDto> GetConfigAsync(CancellationToken cancellationToken);
    Task<JellyfinConfigDto> UpdateConfigAsync(UpdateJellyfinConfigRequest request, CancellationToken cancellationToken);
    Task<bool> ValidateUserAsync(string jellyfinUserId, CancellationToken cancellationToken);
    Task<JellyfinDashboardDto> GetDashboardAsync(Guid userId, CancellationToken cancellationToken);
    Task<(byte[] Content, string ContentType)?> GetImageAsync(Guid userId, string itemId, CancellationToken cancellationToken);
}
