using Household.Api.Models.Integrations;

namespace Household.Api.Application.Interfaces;

public interface IIntegrationRegistry
{
    IIntegrationHealthClient? GetHealthClient(IntegrationType type);
    IReadOnlyList<IIntegrationHealthClient> GetHealthClients();
}
