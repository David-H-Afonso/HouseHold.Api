using Household.Api.Application.Interfaces;
using Household.Api.DTOs;

namespace Household.Api.Infrastructure.Integrations.WarcraftArchive;

public sealed class WarcraftArchiveClient(HttpClient httpClient, IHouseholdProviderAccessService connectionAccess)
    : HouseholdProviderClientBase(httpClient, connectionAccess, "warcraft-archive", "Warcraft Archive"),
        IWarcraftArchiveClient
{
    public Task<WarcraftQuickStatusDto> GetQuickStatusAsync(Guid userId, CancellationToken cancellationToken) =>
        GetRequiredAsync<WarcraftQuickStatusDto>(userId, "dashboard.read", "/dashboard/quick-status", cancellationToken);
}
