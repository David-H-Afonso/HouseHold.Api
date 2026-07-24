using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IJellywatchClient
{
    Task<JellywatchDashboardDto> GetDashboardAsync(Guid userId, string timeZoneId, CancellationToken cancellationToken);
    Task<(byte[] Content, string ContentType)?> GetPosterAsync(Guid userId, long mediaItemId, string requiredScope, CancellationToken cancellationToken);
}
