using System.Diagnostics;
using System.Text.Json;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.DTOs;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.Docker;

public class DockerClient : IDockerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DockerSettings _settings;
    private readonly ILogger<DockerClient> _logger;

    public DockerClient(IOptions<DockerSettings> settings, ILogger<DockerClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ContainerStatusDto>> InspectContainersAsync(
        IReadOnlyList<string> containerNames,
        CancellationToken cancellationToken
    )
    {
        if (!IsEnabled() || containerNames.Count == 0)
            return containerNames.Select(Unknown).ToList();

        var safeNames = containerNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(IsSafeContainerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (safeNames.Count == 0)
            return [];

        try
        {
            var output = await RunDockerInspectAsync(safeNames, cancellationToken);
            var inspected = JsonSerializer.Deserialize<List<DockerInspectContainer>>(output, JsonOptions) ?? [];
            var byName = inspected
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => NormalizeContainerName(item.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => ToStatus(group.First()),
                    StringComparer.OrdinalIgnoreCase
                );

            return safeNames.Select(name => byName.TryGetValue(name, out var status) ? status : Unknown(name)).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or TimeoutException)
        {
            _logger.LogWarning(ex, "Docker inspect failed for allowlisted containers; retrying individually");
            return await InspectIndividuallyAsync(safeNames, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ContainerStatusDto>> InspectIndividuallyAsync(
        IReadOnlyList<string> containerNames,
        CancellationToken cancellationToken)
    {
        var result = new List<ContainerStatusDto>(containerNames.Count);
        foreach (var name in containerNames)
        {
            try
            {
                var output = await RunDockerInspectAsync([name], cancellationToken);
                var inspected = JsonSerializer.Deserialize<List<DockerInspectContainer>>(output, JsonOptions) ?? [];
                result.Add(inspected.FirstOrDefault() is { } container ? ToStatus(container) : Unknown(name));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or TimeoutException)
            {
                result.Add(Unknown(name));
            }
        }
        return result;
    }

    private bool IsEnabled() =>
        _settings.Mode.Equals("compose-cli", StringComparison.OrdinalIgnoreCase)
        || _settings.Mode.Equals("docker-api", StringComparison.OrdinalIgnoreCase);

    private async Task<string> RunDockerInspectAsync(IReadOnlyList<string> containerNames, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.CommandTimeoutSeconds, 5, 300));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_settings.ComposeBin) ? "docker" : _settings.ComposeBin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("inspect");
        foreach (var name in containerNames)
            startInfo.ArgumentList.Add(name);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Docker process did not start.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException ex)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may already have exited.
            }

            throw new TimeoutException("Docker inspect timed out.", ex);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Docker inspect failed: {stderr}");

        return stdout;
    }

    private static bool IsSafeContainerName(string name) =>
        name.Length <= 128 && name.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static ContainerStatusDto ToStatus(DockerInspectContainer container) =>
        new(
            NormalizeContainerName(container.Name),
            container.State?.Status ?? "unknown",
            container.State?.Health?.Status,
            container.Config?.Image,
            ExtractPorts(container.NetworkSettings?.Ports),
            ParseDate(container.State?.StartedAt)
        );

    private static IReadOnlyList<string> ExtractPorts(Dictionary<string, JsonElement>? ports)
    {
        if (ports is null)
            return [];

        return ports.Keys.OrderBy(port => port).ToList();
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;

    private static string NormalizeContainerName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim().TrimStart('/');

    private static ContainerStatusDto Unknown(string name) => new(name, "unknown", null, null, [], null);

    private sealed class DockerInspectContainer
    {
        public string? Name { get; set; }
        public DockerInspectConfig? Config { get; set; }
        public DockerInspectState? State { get; set; }
        public DockerInspectNetworkSettings? NetworkSettings { get; set; }
    }

    private sealed class DockerInspectConfig
    {
        public string? Image { get; set; }
    }

    private sealed class DockerInspectState
    {
        public string? Status { get; set; }
        public string? StartedAt { get; set; }
        public DockerInspectHealth? Health { get; set; }
    }

    private sealed class DockerInspectHealth
    {
        public string? Status { get; set; }
    }

    private sealed class DockerInspectNetworkSettings
    {
        public Dictionary<string, JsonElement>? Ports { get; set; }
    }
}
