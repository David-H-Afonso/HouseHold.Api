using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public class IntegrationService : IIntegrationService
{
    private readonly AppDbContext _db;
    private readonly ISecretProtector _secretProtector;

    public IntegrationService(AppDbContext db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public async Task<IReadOnlyList<IntegrationResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var integrations = await _db
            .Integrations.Include(i => i.Secrets)
            .OrderBy(i => i.Type)
            .ThenBy(i => i.Name)
            .ToListAsync(cancellationToken);

        return integrations.Select(ToResponse).ToList();
    }

    public async Task<IntegrationResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var integration = await _db
            .Integrations.Include(i => i.Secrets)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        return integration is null ? null : ToResponse(integration);
    }

    public async Task<IntegrationResponse> CreateAsync(
        UpsertIntegrationRequest request,
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var integration = new Integration
        {
            Type = request.Type,
            Name = request.Name.Trim(),
            BaseUrl = NormalizeOptional(request.BaseUrl),
            OpenUrl = NormalizeOptional(request.OpenUrl),
            Enabled = request.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };

        ApplySecrets(integration, request.Secrets, now);
        _db.Integrations.Add(integration);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(integration);
    }

    public async Task<IntegrationResponse?> UpdateAsync(
        Guid id,
        UpsertIntegrationRequest request,
        CancellationToken cancellationToken
    )
    {
        var integration = await _db
            .Integrations.Include(i => i.Secrets)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null)
            return null;

        var now = DateTime.UtcNow;
        integration.Type = request.Type;
        integration.Name = request.Name.Trim();
        integration.BaseUrl = NormalizeOptional(request.BaseUrl);
        integration.OpenUrl = NormalizeOptional(request.OpenUrl);
        integration.Enabled = request.Enabled;
        integration.UpdatedAt = now;

        ApplySecrets(integration, request.Secrets, now);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(integration);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var integration = await _db.Integrations.FindAsync([id], cancellationToken);
        if (integration is null)
            return false;

        _db.Integrations.Remove(integration);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ApplySecrets(Integration integration, Dictionary<string, string>? secrets, DateTime now)
    {
        if (secrets is null)
            return;

        foreach (var (rawKey, rawValue) in secrets)
        {
            var key = rawKey.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(rawValue))
                continue;

            var existing = integration.Secrets.FirstOrDefault(s => s.SecretKey == key);
            if (existing is null)
            {
                integration.Secrets.Add(
                    new IntegrationSecret
                    {
                        SecretKey = key,
                        ProtectedValue = _secretProtector.Protect(rawValue),
                        CreatedAt = now,
                        UpdatedAt = now,
                    }
                );
            }
            else
            {
                existing.ProtectedValue = _secretProtector.Protect(rawValue);
                existing.UpdatedAt = now;
            }
        }
    }

    private static IntegrationResponse ToResponse(Integration integration) =>
        new(
            integration.Id,
            integration.Type,
            integration.Name,
            integration.BaseUrl,
            integration.OpenUrl,
            integration.Enabled,
            integration.LastHealthStatus,
            integration.LastCheckedAt,
            integration.CreatedAt,
            integration.UpdatedAt,
            integration.Secrets.Select(s => s.SecretKey).OrderBy(k => k).ToList()
        );

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
