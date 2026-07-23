using Household.Api.Models.Integrations;

namespace Household.Api.Application.Interfaces;

public interface IIntegrationClient
{
    IntegrationType Type { get; }
}
