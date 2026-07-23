using System.Net;
using System.Text;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.Infrastructure.Integrations.BeastVault;
using Household.Api.Infrastructure.Integrations.DoIt;
using Household.Api.Infrastructure.Integrations.Jellywatch;
using Household.Api.Infrastructure.Integrations.WarcraftArchive;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public class HouseholdModuleClientTests
{
    [Fact]
    public async Task DoItNow_UsesCurrentUsersReadTokenAndMapsCanonicalTasks()
    {
        var userId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "date":"2026-07-23","scope":"me",
              "progress":{"total":1,"done":0,"missed":0,"notApplicable":0,"pending":1},
              "zones":[{"available":[{"occurrenceId":"11111111-1111-1111-1111-111111111111","id":"22222222-2222-2222-2222-222222222222","title":"Wash dishes","zoneName":"Kitchen","scope":"Personal","occurrenceStatus":"Pending","occurrenceDate":"2026-07-23"}],"overdue":[],"unavailable":[],"completed":[]}],
              "upcoming":[]
            }
            """));
        var access = new StubAccessService("doit-token");
        var client = new DoItClient(new HttpClient(handler), access);

        var result = await client.GetNowAsync(userId, "2026-07-23", CancellationToken.None);

        Assert.Equal("me", result.Scope);
        Assert.Equal("Available", Assert.Single(result.Tasks).State);
        Assert.Equal("/api/integrations/household/v1/now", handler.Requests.Single().Uri?.AbsolutePath);
        Assert.Equal("date=2026-07-23", handler.Requests.Single().Uri?.Query.TrimStart('?'));
        AssertAccess(access, userId, "doit", "tasks.read");
    }

    [Fact]
    public async Task JellywatchDashboard_RequestsThreeActivitiesAndBuildsPublicLinks()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "profile":{"displayName":"David","totalSeriesWatching":2,"totalSeriesCompleted":3,"totalMoviesSeen":4,"totalEpisodesSeen":5},
              "activity":[{"eventId":7,"title":"Arcane","mediaType":"series","eventType":"Finished","timestamp":"2026-07-23T12:00:00Z"}],
              "upcoming":[{"mediaItemId":9,"seriesId":4,"seriesTitle":"Arcane","seasonNumber":2,"episodeNumber":3,"airDate":"2026-07-24","airTimeUtc":"19:00","batchCount":1}]
            }
            """));
        var access = new StubAccessService("jelly-token");
        var client = new JellywatchClient(
            new HttpClient(handler),
            access,
            Options.Create(new HouseholdConnectionSettings { JellywatchOpenUrl = "https://jelly.example" })
        );

        var result = await client.GetDashboardAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Single(result.Activity);
        Assert.Equal("https://jelly.example/api/asset/9/Poster", Assert.Single(result.Upcoming).PosterUrl);
        Assert.Contains("activityLimit=3", handler.Requests.Single().Uri?.Query);
        Assert.Contains("upcomingDays=30", handler.Requests.Single().Uri?.Query);
        Assert.Equal("activity.read", access.Requests.Single().Scope);
    }

    [Fact]
    public async Task WarcraftQuickStatus_UsesDashboardScopeAndMapsEveryCounter()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {"total":12,"notStarted":1,"pending":2,"inProgress":3,"lastDay":1,"lastWeek":2,"finished":3,"generatedAtUtc":"2026-07-23T12:00:00Z"}
            """));
        var access = new StubAccessService("warcraft-token");
        var client = new WarcraftArchiveClient(new HttpClient(handler), access);

        var result = await client.GetQuickStatusAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(12, result.Total);
        Assert.Equal(1, result.LastDay);
        Assert.Equal(2, result.LastWeek);
        Assert.Equal("/dashboard/quick-status", handler.Requests.Single().Uri?.AbsolutePath);
        Assert.Equal("dashboard.read", access.Requests.Single().Scope);
    }

    [Fact]
    public async Task BeastVaultSearch_EncodesTagFiltersAndResolvesSafePublicAssets()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {"items":[{"id":25,"speciesId":25,"speciesName":"Pikachu","level":20,"isShiny":false,"favorite":true,"isEgg":false,"type1":"Electric","spriteUrl":"/sprites/pokemon/home/25.png","tags":[{"id":3,"name":"Team","colorHex":"#facc15"}]}],"total":1}
            """));
        var access = new StubAccessService("beast-token");
        var client = new BeastVaultClient(
            new HttpClient(handler),
            access,
            Options.Create(new HouseholdConnectionSettings { BeastVaultOpenUrl = "https://bv.example" })
        );

        var result = await client.GetPokemonAsync(Guid.NewGuid(), "pika chu", [3, 8], 0, 24, CancellationToken.None);

        var pokemon = Assert.Single(result.Items);
        Assert.Equal("https://bv.example/sprites/pokemon/home/25.png", pokemon.SpriteUrl);
        Assert.Equal("https://bv.example/pokemon/25", pokemon.OpenUrl);
        var query = handler.Requests.Single().Uri?.Query ?? string.Empty;
        Assert.Contains("search=pika%20chu", query);
        Assert.Contains("tagIds=3", query);
        Assert.Contains("tagIds=8", query);
        Assert.Equal("pokemon.read", access.Requests.Single().Scope);
    }

    [Fact]
    public async Task UnauthorizedProviderResponse_RefreshesOnce()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler(_ => ++responseNumber == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : JsonResponse("""{"total":0,"notStarted":0,"pending":0,"inProgress":0,"lastDay":0,"lastWeek":0,"finished":0,"generatedAtUtc":"2026-07-23T12:00:00Z"}"""));
        var access = new StubAccessService("old-token", "rotated-token");
        var client = new WarcraftArchiveClient(new HttpClient(handler), access);

        await client.GetQuickStatusAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("old-token", handler.Requests[0].Authorization?.Parameter);
        Assert.Equal("rotated-token", handler.Requests[1].Authorization?.Parameter);
        Assert.False(access.Requests[0].ForceRefresh);
        Assert.True(access.Requests[1].ForceRefresh);
        Assert.Equal("version-1", access.Requests[1].FailedTokenVersion);
    }

    private static void AssertAccess(
        StubAccessService access,
        Guid userId,
        string provider,
        string scope
    )
    {
        var request = Assert.Single(access.Requests);
        Assert.Equal(userId, request.UserId);
        Assert.Equal(provider, request.Provider);
        Assert.Equal(scope, request.Scope);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubAccessService(params string[] tokens) : IHouseholdProviderAccessService
    {
        private int _requestIndex;
        public List<AccessRequest> Requests { get; } = [];

        public Task<HouseholdProviderAccessResult> GetAccessAsync(
            Guid userId,
            string providerId,
            string requiredScope,
            bool forceRefresh,
            string? failedTokenVersion,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(new AccessRequest(userId, providerId, requiredScope, forceRefresh, failedTokenVersion));
            var index = Math.Min(_requestIndex++, tokens.Length - 1);
            return Task.FromResult(new HouseholdProviderAccessResult(
                HouseholdProviderAccessStatus.Success,
                tokens[index],
                "https://provider-api.example",
                $"version-{index + 1}"
            ));
        }
    }

    private sealed record AccessRequest(
        Guid UserId,
        string Provider,
        string Scope,
        bool ForceRefresh,
        string? FailedTokenVersion
    );

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(new RecordedRequest(request.RequestUri, request.Headers.Authorization));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record RecordedRequest(
        Uri? Uri,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization
    );
}
