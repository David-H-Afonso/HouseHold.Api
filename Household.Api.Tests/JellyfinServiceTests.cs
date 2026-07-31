using System.Net;
using System.Text;
using Household.Api.DTOs;
using Household.Api.Infrastructure.Integrations.Jellyfin;
using Household.Api.Models.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Household.Api.Tests;

public sealed class JellyfinServiceTests
{
    [Fact]
    public async Task EmptyContinueWatching_FallsBackToAllNextUpWithoutExposingApiKey()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var user = await fixture.AddUserAsync("viewer@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference { UserId = user.Id, JellyfinUserId = "jelly-user" });
        await fixture.Db.SaveChangesAsync();
        var handler = new JellyfinHandler();
        var service = new JellyfinService(
            fixture.Db,
            new HttpClient(handler),
            new EphemeralDataProtectionProvider(),
            new JellyfinImageGrants()
        );

        var config = await service.UpdateConfigAsync(
            new UpdateJellyfinConfigRequest("https://jelly.internal", "https://jelly.example", "server-secret-key"),
            CancellationToken.None
        );
        var dashboard = await service.GetDashboardAsync(user.Id, CancellationToken.None);

        Assert.True(config.HasApiKey);
        Assert.DoesNotContain("server-secret-key", fixture.Db.IntegrationSecrets.Single().ProtectedValue);
        Assert.True(dashboard.UsedNextUpFallback);
        Assert.Equal("https://jelly.example", dashboard.OpenUrl);
        Assert.Equal(4, dashboard.DashboardItems.Count);
        Assert.All(dashboard.DashboardItems, item =>
        {
            Assert.StartsWith("/api/v1/jellyfin/images/", item.ImageUrl);
            Assert.DoesNotContain("server-secret-key", item.ImageUrl);
            Assert.Contains($"details?id={item.Id}", item.OpenUrl);
        });
        Assert.All(handler.ApiKeys, key => Assert.Equal("server-secret-key", key));
    }

    [Fact]
    public async Task TwoHouseholdUsers_UseOnlyTheirOwnJellyfinMapping()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var userA = await fixture.AddUserAsync("a@example.test");
        var userB = await fixture.AddUserAsync("b@example.test");
        fixture.Db.UserPreferences.AddRange(
            new UserPreference { UserId = userA.Id, JellyfinUserId = "jelly-a" },
            new UserPreference { UserId = userB.Id, JellyfinUserId = "jelly-b" }
        );
        await fixture.Db.SaveChangesAsync();
        var handler = new JellyfinHandler();
        var service = CreateService(fixture, handler, new JellyfinImageGrants());
        await ConfigureAsync(service);

        await service.GetDashboardAsync(userA.Id, CancellationToken.None);
        await service.GetDashboardAsync(userB.Id, CancellationToken.None);

        Assert.Contains(handler.Paths, path => path.Contains("/Users/jelly-a/Items/Resume", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, path => path.Contains("UserId=jelly-a", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, path => path.Contains("/Users/jelly-b/Items/Resume", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, path => path.Contains("UserId=jelly-b", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Paths, path => path.Contains("/Users/jelly-a/Items/Resume", StringComparison.Ordinal) && path.Contains("jelly-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImageGrant_IsDeniedBeforeProviderCallForAnotherUser()
    {
        await using var fixture = await UserSettingsServiceTests.TestDb.CreateAsync();
        var userA = await fixture.AddUserAsync("a@example.test");
        var userB = await fixture.AddUserAsync("b@example.test");
        fixture.Db.UserPreferences.Add(new UserPreference { UserId = userA.Id, JellyfinUserId = "jelly-a" });
        await fixture.Db.SaveChangesAsync();
        var handler = new JellyfinHandler();
        var grants = new JellyfinImageGrants();
        var service = CreateService(fixture, handler, grants);
        await ConfigureAsync(service);
        await service.GetDashboardAsync(userA.Id, CancellationToken.None);
        var requestsBeforeImage = handler.Paths.Count;

        var denied = await service.GetImageAsync(userB.Id, "one", CancellationToken.None);

        Assert.Null(denied);
        Assert.Equal(requestsBeforeImage, handler.Paths.Count);
    }

    private static JellyfinService CreateService(
        UserSettingsServiceTests.TestDb fixture,
        HttpMessageHandler handler,
        JellyfinImageGrants grants
    ) => new(fixture.Db, new HttpClient(handler), new EphemeralDataProtectionProvider(), grants);

    private static Task<JellyfinConfigDto> ConfigureAsync(JellyfinService service) => service.UpdateConfigAsync(
        new UpdateJellyfinConfigRequest("https://jelly.internal", "https://jelly.example", "server-secret-key"),
        CancellationToken.None
    );

    private sealed class JellyfinHandler : HttpMessageHandler
    {
        public List<string?> ApiKeys { get; } = [];
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ApiKeys.Add(request.Headers.TryGetValues("X-Emby-Token", out var values) ? values.Single() : null);
            Paths.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            var json = request.RequestUri?.AbsolutePath.EndsWith("/Resume", StringComparison.Ordinal) == true
                ? "{\"Items\":[]}"
                : """{"Items":[{"Id":"one","Name":"One"},{"Id":"two","Name":"Two"},{"Id":"three","Name":"Three"},{"Id":"four","Name":"Four"}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
