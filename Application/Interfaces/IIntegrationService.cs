using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IIntegrationService
{
    Task<IReadOnlyList<IntegrationResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<IntegrationResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IntegrationResponse> CreateAsync(UpsertIntegrationRequest request, CancellationToken cancellationToken);
    Task<IntegrationResponse?> UpdateAsync(Guid id, UpsertIntegrationRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
