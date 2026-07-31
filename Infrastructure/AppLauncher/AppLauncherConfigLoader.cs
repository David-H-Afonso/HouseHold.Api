using System.Text.Json;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.AppLauncher;

public class AppLauncherConfigLoader : IAppLauncherConfigLoader
{
    private const long MaxConfigBytes = 1024 * 1024;
    private const int MaxConfiguredItems = 20;
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
            return BuildBuiltInItems();

        try
        {
            var file = new FileInfo(_settings.ConfigPath);
            if (file.Length > MaxConfigBytes)
            {
                _logger.LogWarning("App launcher config exceeds the {MaxBytes} byte limit at {Path}", MaxConfigBytes, _settings.ConfigPath);
                return BuildBuiltInItems();
            }
            await using var stream = file.OpenRead();
            var items = await JsonSerializer.DeserializeAsync<List<AppLauncherConfigItem>>(
                stream,
                JsonOptions,
                cancellationToken
            );

            var configuredItems = (items ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(MaxConfiguredItems)
                .ToList();
            foreach (var item in configuredItems)
            {
                item.Id = item.Id.Trim();
                item.Name = item.Name.Trim();
            }

            return MergeWithBuiltIns(configuredItems);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "App launcher config JSON is invalid at {Path}", _settings.ConfigPath);
            return BuildBuiltInItems();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "App launcher config could not be read at {Path}", _settings.ConfigPath);
            return BuildBuiltInItems();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "App launcher config is not readable at {Path}", _settings.ConfigPath);
            return BuildBuiltInItems();
        }
    }

    private IReadOnlyList<AppLauncherConfigItem> MergeWithBuiltIns(
        IReadOnlyList<AppLauncherConfigItem> configuredItems
    )
    {
        var builtIns = BuildBuiltInItems();
        var configuredById = configuredItems.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var builtInIds = builtIns.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = builtIns
            .Select(item => configuredById.TryGetValue(item.Id, out var configured)
                ? MergeBuiltIn(item, configured)
                : item)
            .ToList();

        merged.AddRange(configuredItems.Where(item => !builtInIds.Contains(item.Id)));
        return merged;
    }

    private IReadOnlyList<AppLauncherConfigItem> BuildBuiltInItems()
    {
        return
        [
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
            CreateProvider("doit", "DoIt", "Tasks", "Task planning and routines", _connection.DoItBaseUrl, _connection.DoItOpenUrl, "api/health"),
            CreateProvider("gamesdatabase", "Games Database", "Games", "Personal game collection", _connection.GamesDatabaseBaseUrl, _connection.GamesDatabaseOpenUrl),
            CreateProvider("jellywatch", "Jellywatch", "Media", "Watch tracking and ratings", _connection.JellywatchBaseUrl, _connection.JellywatchOpenUrl),
            CreateProvider("beastvault", "Beast Vault", "Collections", "Pokemon collection manager", _connection.BeastVaultBaseUrl, _connection.BeastVaultOpenUrl),
            CreateProvider("warcraftarchive", "Warcraft Archive", "Collections", "World of Warcraft progress tracker", _connection.WarcraftArchiveBaseUrl, _connection.WarcraftArchiveOpenUrl),
        ];
    }

    private static AppLauncherConfigItem MergeBuiltIn(
        AppLauncherConfigItem canonical,
        AppLauncherConfigItem configured
    ) => new()
    {
        Id = canonical.Id,
        Name = configured.Name,
        Category = string.IsNullOrWhiteSpace(configured.Category) ? canonical.Category : configured.Category,
        Description = configured.Description ?? canonical.Description,
        IconUrl = configured.IconUrl ?? canonical.IconUrl,
        InternalUrl = configured.InternalUrl ?? canonical.InternalUrl,
        OpenUrl = canonical.OpenUrl,
        Favorite = configured.Favorite,
        HealthCheckUrl = configured.HealthCheckUrl ?? canonical.HealthCheckUrl,
        ContainerNames = configured.ContainerNames ?? canonical.ContainerNames,
        ComposePath = configured.ComposePath ?? canonical.ComposePath,
        AdminActionsEnabled = configured.AdminActionsEnabled,
    };

    private static AppLauncherConfigItem CreateProvider(
        string id,
        string name,
        string category,
        string description,
        string? apiUrl,
        string? openUrl,
        string healthPath = "health"
    ) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        Description = description,
        InternalUrl = openUrl,
        OpenUrl = openUrl,
        HealthCheckUrl = AppendPath(apiUrl, healthPath),
        Favorite = true,
    };

    private static string? AppendPath(string? baseUrl, string path) =>
        string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/{path}";
}
