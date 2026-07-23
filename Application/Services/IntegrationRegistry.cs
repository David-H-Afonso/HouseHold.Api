using Household.Api.Application.Interfaces;
using Household.Api.Models.Integrations;

namespace Household.Api.Application.Services;

public class IntegrationRegistry : IIntegrationRegistry
{
    private readonly IReadOnlyDictionary<IntegrationType, IIntegrationHealthClient> _healthClients;

    public IntegrationRegistry(IEnumerable<IIntegrationHealthClient> healthClients)
    {
        _healthClients = healthClients.GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First());
    }

    public IIntegrationHealthClient? GetHealthClient(IntegrationType type) =>
        _healthClients.TryGetValue(type, out var client) ? client : null;

    public IReadOnlyList<IIntegrationHealthClient> GetHealthClients() => _healthClients.Values.ToList();
}
