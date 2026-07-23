using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IIntegrationHealthService
{
    Task<IntegrationHealthDto> GetHealthAsync(Guid integrationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<IntegrationHealthDto>> GetAllHealthAsync(CancellationToken cancellationToken);
}
