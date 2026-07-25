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
    private readonly HouseholdConnectionSettings _connection;
    private readonly ILogger<AppLauncherConfigLoader> _logger;

    public AppLauncherConfigLoader(
        IOptions<AppLauncherSettings> settings,
        IOptions<HouseholdConnectionSettings> connection,
        ILogger<AppLauncherConfigLoader> logger
    )
    {
        _settings = settings.Value;
        _connection = connection.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AppLauncherConfigItem>> LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ConfigPath) || !File.Exists(_settings.ConfigPath))
            return BuildDefaultItems();

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

            var configuredItems = (items ?? [])
                .Take(20)
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .ToList();

            return configuredItems.Count > 0 ? configuredItems : BuildDefaultItems();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "App launcher config JSON is invalid at {Path}", _settings.ConfigPath);
            return BuildDefaultItems();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "App launcher config could not be read at {Path}", _settings.ConfigPath);
            return BuildDefaultItems();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "App launcher config is not readable at {Path}", _settings.ConfigPath);
            return BuildDefaultItems();
        }
    }

    private IReadOnlyList<AppLauncherConfigItem> BuildDefaultItems()
    {
        var candidates = new[]
        {
            new AppLauncherConfigItem
            {
                Id = "household",
                Name = "Household",
                Category = "Core",
                Description = "Central home dashboard",
                OpenUrl = _connection.PublicUrl,
                InternalUrl = _connection.PublicUrl,
                HealthCheckUrl = AppendPath(_connection.ApiPublicUrl, "health"),
                Favorite = true,
            },
            CreateProvider("doit", "DoIt", "Tasks", "Task planning and routines", _connection.DoItBaseUrl, _connection.DoItOpenUrl),
            CreateProvider("gamesdatabase", "Games Database", "Games", "Personal game collection", _connection.GamesDatabaseBaseUrl, _connection.GamesDatabaseOpenUrl),
            CreateProvider("jellywatch", "Jellywatch", "Media", "Watch tracking and ratings", _connection.JellywatchBaseUrl, _connection.JellywatchOpenUrl),
            CreateProvider("beastvault", "Beast Vault", "Collections", "Pokemon collection manager", _connection.BeastVaultBaseUrl, _connection.BeastVaultOpenUrl),
            CreateProvider("warcraftarchive", "Warcraft Archive", "Collections", "World of Warcraft progress tracker", _connection.WarcraftArchiveBaseUrl, _connection.WarcraftArchiveOpenUrl),
        };

        return candidates
            .Where(item => !string.IsNullOrWhiteSpace(item.OpenUrl))
            .ToList();
    }

    private static AppLauncherConfigItem CreateProvider(
        string id,
        string name,
        string category,
        string description,
        string? internalUrl,
        string? openUrl
    ) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        Description = description,
        InternalUrl = internalUrl,
        ExternalUrl = openUrl,
        OpenUrl = openUrl,
        HealthCheckUrl = AppendPath(internalUrl, "health"),
        Favorite = true,
    };

    private static string? AppendPath(string? baseUrl, string path) =>
        string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/{path}";
}
