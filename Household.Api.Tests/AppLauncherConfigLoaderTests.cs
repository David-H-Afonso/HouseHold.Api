using Household.Api.Configuration;
using Household.Api.Infrastructure.AppLauncher;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public sealed class AppLauncherConfigLoaderTests
{
    [Fact]
    public async Task Load_MergesOwnedMetadataWithCanonicalUrlsAndRetainsThirdPartyApps()
    {
        var path = await WriteConfigAsync("""
            [
              {
                "id": "household",
                "name": "Household Home",
                "category": "Dashboards",
                "description": "Mounted metadata",
                "iconUrl": "/icons/household.svg",
                "internalUrl": "http://household-front:3000",
                "openUrl": "https://stale-household.example",
                "healthCheckUrl": "http://household-api:8080/health",
                "containerNames": ["household-api", "household-front"],
                "composePath": "/DATA/AppData/household/docker-compose.yml",
                "favorite": true
              },
              {
                "id": "gamesdatabase",
                "name": "Games Database",
                "category": "Games",
                "iconUrl": "/icons/gamesdatabase.svg",
                "openUrl": "https://stale-games.example"
              },
              {
                "id": "jellyfin",
                "name": "Jellyfin",
                "category": "Media",
                "openUrl": "https://jellyfin.example"
              }
            ]
            """);

        try
        {
            var loader = CreateLoader(path, new HouseholdConnectionSettings
            {
                PublicUrl = "https://household.example",
                ApiPublicUrl = "https://household-api.example",
                DoItOpenUrl = "https://doit.example",
                GamesDatabaseOpenUrl = "https://games.example",
                JellywatchOpenUrl = "https://jellywatch.example",
                BeastVaultOpenUrl = "https://beastvault.example",
                WarcraftArchiveOpenUrl = "https://warcraft.example",
            });

            var items = await loader.LoadAsync(CancellationToken.None);

            Assert.Equal(7, items.Count);
            var household = items.Single(item => item.Id == "household");
            Assert.Equal("https://household.example", household.OpenUrl);
            Assert.Equal("Household Home", household.Name);
            Assert.Equal("http://household-front:3000", household.InternalUrl);
            Assert.Equal("http://household-api:8080/health", household.HealthCheckUrl);
            Assert.Equal(["household-api", "household-front"], household.ContainerNames);
            Assert.Equal("/DATA/AppData/household/docker-compose.yml", household.ComposePath);
            Assert.Equal("https://games.example", items.Single(item => item.Id == "gamesdatabase").OpenUrl);
            Assert.Equal("https://doit.example", items.Single(item => item.Id == "doit").OpenUrl);
            Assert.Equal("https://jellyfin.example", items.Single(item => item.Id == "jellyfin").OpenUrl);
            Assert.Equal("https://games.example/favicon.ico", items.Single(item => item.Id == "gamesdatabase").IconUrl);
            Assert.Equal("https://doit.example/doit-icon.svg", items.Single(item => item.Id == "doit").IconUrl);
            Assert.Equal("https://jellywatch.example/logo.png", items.Single(item => item.Id == "jellywatch").IconUrl);
            Assert.Equal("/household-mark.svg", household.IconUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_DoesNotFallBackToMountedUrlWhenCanonicalUrlIsMissing()
    {
        var path = await WriteConfigAsync("""
            [
              {
                "id": "beastvault",
                "name": "Beast Vault",
                "category": "Collections",
                "externalUrl": "https://legacy-beastvault.example",
                "openUrl": "https://stale-beastvault.example"
              }
            ]
            """);

        try
        {
            var items = await CreateLoader(path, new HouseholdConnectionSettings()).LoadAsync(CancellationToken.None);

            Assert.Equal(6, items.Count);
            Assert.Null(items.Single(item => item.Id == "beastvault").OpenUrl);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Load_RetainsTwentyConfiguredThirdPartyAppsAlongsideBuiltIns()
    {
        var configured = Enumerable.Range(1, 20)
            .Select(index => $$"""{"id":"third-party-{{index}}","name":"Third party {{index}}","openUrl":"https://app{{index}}.example"}""");
        var path = await WriteConfigAsync($"[{string.Join(',', configured)}]");

        try
        {
            var items = await CreateLoader(path, new HouseholdConnectionSettings()).LoadAsync(CancellationToken.None);

            Assert.Equal(26, items.Count);
            Assert.Equal(20, items.Count(item => item.Id.StartsWith("third-party-", StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AppLauncherConfigLoader CreateLoader(string path, HouseholdConnectionSettings connection) =>
        new(
            Options.Create(new AppLauncherSettings { ConfigPath = path }),
            Options.Create(connection),
            NullLogger<AppLauncherConfigLoader>.Instance
        );

    private static async Task<string> WriteConfigAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"household-app-launcher-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        return path;
    }
}
