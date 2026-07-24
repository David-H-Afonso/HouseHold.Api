using System.Text.Json;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.AppLauncher;

public class AppLauncherConfigLoader : IAppLauncherConfigLoader
{
    private const long MaxConfigBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppLauncherSettings _settings;
    private readonly ILogger<AppLauncherConfigLoader> _logger;

    public AppLauncherConfigLoader(IOptions<AppLauncherSettings> settings, ILogger<AppLauncherConfigLoader> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AppLauncherConfigItem>> LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ConfigPath) || !File.Exists(_settings.ConfigPath))
            return [];

        try
        {
            var file = new FileInfo(_settings.ConfigPath);
            if (file.Length > MaxConfigBytes)
            {
                _logger.LogWarning("App launcher config exceeds the {MaxBytes} byte limit at {Path}", MaxConfigBytes, _settings.ConfigPath);
                return [];
            }
            await using var stream = file.OpenRead();
            var items = await JsonSerializer.DeserializeAsync<List<AppLauncherConfigItem>>(
                stream,
                JsonOptions,
                cancellationToken
            );

            return (items ?? [])
                .Take(20)
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "App launcher config JSON is invalid at {Path}", _settings.ConfigPath);
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "App launcher config could not be read at {Path}", _settings.ConfigPath);
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "App launcher config is not readable at {Path}", _settings.ConfigPath);
            return [];
        }
    }
}
