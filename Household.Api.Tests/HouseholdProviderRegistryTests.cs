using Household.Api.Application.Services;
using Household.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public class HouseholdProviderRegistryTests
{
    [Fact]
    public void Registry_UsesFixedProvidersScopesAndHashRoutes()
    {
        var registry = new HouseholdProviderRegistry(
            Options.Create(
                new HouseholdConnectionSettings
                {
                    PublicUrl = "https://household.example",
                    ApiPublicUrl = "https://api.household.example/",
                    DoItBaseUrl = "https://doit-api.example",
                    DoItOpenUrl = "https://doit.example",
                    GamesDatabaseBaseUrl = "https://games-api.example",
                    GamesDatabaseOpenUrl = "https://games.example",
                    JellywatchBaseUrl = "https://jelly-api.example",
                    JellywatchOpenUrl = "https://jelly.example",
                    BeastVaultBaseUrl = "https://beast-api.example",
                    BeastVaultOpenUrl = "https://beast.example",
                    WarcraftArchiveBaseUrl = "https://warcraft-api.example",
                    WarcraftArchiveOpenUrl = "https://warcraft.example",
                }
            )
        );

        var providers = registry.GetAll();
        Assert.Equal(
            ["doit", "games-database", "jellywatch", "beast-vault", "warcraft-archive"],
            providers.Select(provider => provider.Id)
        );
        Assert.All(providers, provider => Assert.True(provider.Configured));

        var games = providers.Single(provider => provider.Id == "games-database");
        var authorizationUrl = registry.BuildAuthorizationUrl(games, "state", "challenge");
        Assert.StartsWith("https://games.example/#/integrations/household/authorize?", authorizationUrl);
        Assert.Contains("scope=profile.read%20games.read%20games.status.write", authorizationUrl);
        Assert.Equal(
            "https://api.household.example/integrations/callback/games-database",
            games.RedirectUri
        );
    }
}
