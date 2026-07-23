using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IDashboardAggregationService
{
    Task<DashboardResponse> GetDashboardAsync(Guid userId, CancellationToken cancellationToken);
}
