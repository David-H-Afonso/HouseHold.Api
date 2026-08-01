using System.Text.Json;
using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public sealed class ContainerStatusService(AppDbContext db, IDockerClient dockerClient) : IContainerStatusService
{
    private static readonly SemaphoreSlim DockerInspectionGate = new(4, 4);

    public async Task<IReadOnlyList<ContainerStatusDto>> GetAppContainersAsync(
        string appId,
        CancellationToken cancellationToken)
    {
        var json = await db.AllowedComposeApps.AsNoTracking()
            .Where(item => item.AppId.ToLower() == appId.ToLower())
            .Select(item => item.ContainerNamesJson)
            .FirstOrDefaultAsync(cancellationToken);
        var names = ParseContainerNames(json);
        return names.Count == 0
            ? []
            : await InspectContainersAsync(names, cancellationToken);
    }

    public async Task<IReadOnlyList<AppContainerStatusDto>> GetAllAppStatusesAsync(
        CancellationToken cancellationToken)
    {
        var policies = await db.AllowedComposeApps.AsNoTracking()
            .Select(item => new { item.AppId, item.ContainerNamesJson })
            .ToListAsync(cancellationToken);
        var namesByApp = policies.GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ParseContainerNames(group.First().ContainerNamesJson),
                StringComparer.OrdinalIgnoreCase);
        var allNames = namesByApp.Values.SelectMany(item => item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var inspected = await InspectContainersAsync(allNames, cancellationToken);
        var byName = inspected.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        return namesByApp.Select(item => new AppContainerStatusDto(
                item.Key,
                item.Value.Select(name => byName.TryGetValue(name, out var status)
                        ? status
                        : new ContainerStatusDto(name, "unknown", null, null, [], null))
                    .ToList()))
            .ToList();
    }

    private async Task<IReadOnlyList<ContainerStatusDto>> InspectContainersAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        await DockerInspectionGate.WaitAsync(cancellationToken);
        try
        {
            return await dockerClient.InspectContainersAsync(names, cancellationToken);
        }
        finally
        {
            DockerInspectionGate.Release();
        }
    }

    private static IReadOnlyList<string> ParseContainerNames(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16000) return [];
        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(value) ?? [];
            if (names.Count > 100) return [];
            return names.Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Where(name => name.Length <= 128
                    && name.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
