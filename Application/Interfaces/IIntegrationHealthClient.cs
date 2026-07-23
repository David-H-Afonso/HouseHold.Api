using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IIntegrationHealthClient : IIntegrationClient
{
    Task<IntegrationHealthDto> GetHealthAsync(CancellationToken cancellationToken);
}
