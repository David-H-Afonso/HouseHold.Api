using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public class IntegrationHealthService : IIntegrationHealthService
{
    private readonly AppDbContext _db;
    private readonly IIntegrationRegistry _registry;

    public IntegrationHealthService(AppDbContext db, IIntegrationRegistry registry)
    {
        _db = db;
        _registry = registry;
    }

    public async Task<IntegrationHealthDto> GetHealthAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        var integration = await _db.Integrations.FindAsync([integrationId], cancellationToken);
        if (integration is null || integration.Type == IntegrationType.CasaOS)
            throw new KeyNotFoundException("Integration not found.");

        var health = await ResolveHealthAsync(integration, cancellationToken);
        integration.LastHealthStatus = health.Status;
        integration.LastCheckedAt = health.CheckedAt;
        await _db.SaveChangesAsync(cancellationToken);
        return health;
    }

    public async Task<IReadOnlyList<IntegrationHealthDto>> GetAllHealthAsync(CancellationToken cancellationToken)
    {
        var integrations = await _db.Integrations
            .Where(i => i.Type != IntegrationType.CasaOS)
            .OrderBy(i => i.Type)
            .ThenBy(i => i.Name)
            .ToListAsync(cancellationToken);
        var results = new List<IntegrationHealthDto>();

        foreach (var integration in integrations)
        {
            var health = await ResolveHealthAsync(integration, cancellationToken);
            integration.LastHealthStatus = health.Status;
            integration.LastCheckedAt = health.CheckedAt;
            results.Add(health);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return results;
    }

    private async Task<IntegrationHealthDto> ResolveHealthAsync(
        Integration integration,
        CancellationToken cancellationToken
    )
    {
        var checkedAt = DateTime.UtcNow;

        if (!integration.Enabled)
            return new IntegrationHealthDto(
                integration.Id,
                integration.Type,
                integration.Name,
                IntegrationHealthStatus.Unknown,
                "Integration is disabled.",
                checkedAt
            );

        if (string.IsNullOrWhiteSpace(integration.BaseUrl) && string.IsNullOrWhiteSpace(integration.OpenUrl))
            return new IntegrationHealthDto(
                integration.Id,
                integration.Type,
                integration.Name,
                IntegrationHealthStatus.NotConfigured,
                "Integration is missing BaseUrl/OpenUrl configuration.",
                checkedAt
            );

        var client = _registry.GetHealthClient(integration.Type);
        if (client is null)
            return new IntegrationHealthDto(
                integration.Id,
                integration.Type,
                integration.Name,
                IntegrationHealthStatus.Unknown,
                "No health client is registered for this integration type yet.",
                checkedAt
            );

        try
        {
            return await client.GetHealthAsync(cancellationToken);
        }
        catch (Exception)
        {
            return new IntegrationHealthDto(
                integration.Id,
                integration.Type,
                integration.Name,
                IntegrationHealthStatus.Degraded,
                "Health check failed.",
                DateTime.UtcNow
            );
        }
    }
}
