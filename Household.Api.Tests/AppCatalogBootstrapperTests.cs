using System.Text.Json;
using Household.Api.Application.Interfaces;
using Household.Api.Application.Services;
using Household.Api.Infrastructure.AppLauncher;
using Household.Api.Models.Integrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Household.Api.Tests;

public sealed class AppCatalogBootstrapperTests
{
    [Fact]
    public async Task FreshDatabase_SeedsFullCatalogAndSafeOperationPolicies()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var bootstrapper = CreateBootstrapper(fixture);

        await bootstrapper.EnsureSeededAsync(CancellationToken.None);

        Assert.Equal(25, fixture.Db.AppLauncherItems.Count());
        Assert.Equal(24, fixture.Db.AllowedComposeApps.Count());
        Assert.Equal(
            "https://seerr.davidhormigafonso.work",
            fixture.Db.AppLauncherItems.Single(item => item.AppId == "seerr").OpenUrl);
        Assert.Equal(
            "https://gamesdatabase.davidhormigafonso.work",
            fixture.Db.AppLauncherItems.Single(item => item.AppId == "gamesdatabase").OpenUrl);
        Assert.DoesNotContain(fixture.Db.AllowedComposeApps, item => item.AppId == "casaos");

        var immich = fixture.Db.AllowedComposeApps.Single(item => item.AppId == "immich");
        Assert.False(immich.AdminActionsEnabled);
        var immichActions = JsonSerializer.Deserialize<string[]>(immich.AllowedActionsJson!);
        Assert.NotNull(immichActions);
        Assert.Equal(["monitor"], immichActions);
        Assert.False(CasaOsUpdatePolicy.IsAllowedAppId("immich"));
        Assert.False(CasaOsUpdatePolicy.IsAllowedAppId("casaos"));
        Assert.Equal(
            "big-bear-seerr",
            CasaOsUpdatePolicy.GetProjectName("seerr"));
    }

    [Fact]
    public async Task RepeatedBootstrap_PreservesAdminEditsAndCreatesNoDuplicates()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var bootstrapper = CreateBootstrapper(fixture);
        await bootstrapper.EnsureSeededAsync(CancellationToken.None);
        var seerr = fixture.Db.AppLauncherItems.Single(item => item.AppId == "seerr");
        seerr.Name = "My requests";
        seerr.OpenUrl = "https://custom.example.test";
        await fixture.Db.SaveChangesAsync();

        await bootstrapper.EnsureSeededAsync(CancellationToken.None);

        Assert.Equal(25, fixture.Db.AppLauncherItems.Count());
        Assert.Equal(24, fixture.Db.AllowedComposeApps.Count());
        Assert.Equal("My requests", seerr.Name);
        Assert.Equal("https://custom.example.test", seerr.OpenUrl);
    }

    [Fact]
    public async Task Bootstrap_MigratesLegacyJellyseerrFavoriteToSeerr()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("member@example.test");
        fixture.Db.UserAppFavorites.Add(new UserAppFavorite
        {
            UserId = user.Id,
            AppId = "jellyseerr",
            Favorite = false,
        });
        await fixture.Db.SaveChangesAsync();

        await CreateBootstrapper(fixture).EnsureSeededAsync(CancellationToken.None);

        var favorite = Assert.Single(fixture.Db.UserAppFavorites);
        Assert.Equal("seerr", favorite.AppId);
        Assert.False(favorite.Favorite);
    }

    [Fact]
    public async Task Bootstrap_ImportsMountedMetadataButKeepsCanonicalPreferredUrl()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var mounted = new AppLauncherConfigItem
        {
            Id = "seerr",
            Name = "Family requests",
            Category = "Cinema",
            Description = "Mounted description",
            IconUrl = "/icons/custom-seerr.svg",
            OpenUrl = "https://stale.example",
            Favorite = false,
        };

        await CreateBootstrapper(fixture, [mounted]).EnsureSeededAsync(CancellationToken.None);

        var seerr = fixture.Db.AppLauncherItems.Single(item => item.AppId == "seerr");
        Assert.Equal("Family requests", seerr.Name);
        Assert.Equal("Cinema", seerr.Category);
        Assert.Equal("Mounted description", seerr.Description);
        Assert.Equal("/icons/custom-seerr.svg", seerr.IconUrl);
        Assert.Equal("https://seerr.davidhormigafonso.work", seerr.OpenUrl);
        Assert.False(seerr.Favorite);
    }

    [Fact]
    public async Task Bootstrap_DisablesCaseDuplicateCatalogRowsAndReplacesKnownOperationalTargets()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        fixture.Db.AppLauncherItems.AddRange(
            new AppLauncherItem { AppId = "seerr", Name = "Seerr", Category = "Media", Enabled = true },
            new AppLauncherItem { AppId = "SEERR", Name = "Duplicate", Category = "Media", Enabled = true });
        fixture.Db.AllowedComposeApps.AddRange(
            new AllowedComposeApp
            {
                AppId = "seerr",
                DisplayName = "Unsafe",
                ComposePath = "wrong",
                ProjectName = "wrong",
                HealthCheckUrl = "http://127.0.0.1/private",
            },
            new AllowedComposeApp
            {
                AppId = "SEERR",
                DisplayName = "Duplicate",
                ComposePath = "wrong-again",
                ProjectName = "wrong-again",
                HealthCheckUrl = "http://127.0.0.1/private-again",
            });
        await fixture.Db.SaveChangesAsync();

        await CreateBootstrapper(fixture).EnsureSeededAsync(CancellationToken.None);

        Assert.Single(fixture.Db.AppLauncherItems.Where(item =>
            item.Enabled && item.AppId.ToLower() == "seerr"));
        var policy = Assert.Single(fixture.Db.AllowedComposeApps.Where(item => item.AppId.ToLower() == "seerr"));
        Assert.Equal("big-bear-seerr", policy.ProjectName);
        Assert.Equal("http://seerr:5055/api/v1/status", policy.HealthCheckUrl);
    }

    [Fact]
    public async Task Bootstrap_PersistsConfiguredLanHealthTargetForSplitCasaOsStacks()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var configured = new AppLauncherConfigItem
        {
            Id = "beastvault",
            Name = "Beast Vault",
            InternalUrl = "http://192.168.0.32:8081",
            HealthCheckUrl = "http://192.168.0.32:8081/health",
        };

        await CreateBootstrapper(fixture, [configured]).EnsureSeededAsync(CancellationToken.None);

        var item = fixture.Db.AppLauncherItems.Single(entry => entry.AppId == "beastvault");
        var policy = fixture.Db.AllowedComposeApps.Single(entry => entry.AppId == "beastvault");
        Assert.Equal("http://192.168.0.32:8081", item.InternalUrl);
        Assert.Equal("http://192.168.0.32:8081/health", policy.HealthCheckUrl);
    }

    private static AppCatalogBootstrapper CreateBootstrapper(
        UserSettingsServiceTests.TestDb fixture,
        IReadOnlyList<AppLauncherConfigItem>? items = null) =>
        new(fixture.Db, new StubLoader(items ?? []), NullLogger<AppCatalogBootstrapper>.Instance);

    private sealed class StubLoader(IReadOnlyList<AppLauncherConfigItem> items) : IAppLauncherConfigLoader
    {
        public Task<IReadOnlyList<AppLauncherConfigItem>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(items);
    }
}
