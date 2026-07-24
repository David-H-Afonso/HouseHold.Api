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
              "zones":[{"available":[{"occurrenceId":"11111111-1111-1111-1111-111111111111","id":"22222222-2222-2222-2222-222222222222","title":"Wash dishes","zoneName":"Kitchen","scope":"Personal","occurrenceStatus":"Pending","occurrenceDate":"2026-07-23","assignmentMode":"SpecificUsers","assigneeIds":["33333333-3333-3333-3333-333333333333"],"assigneeNames":["David"],"timeZoneId":"Europe/Madrid","recurrenceType":"Daily","completedAt":"2026-07-23T10:11:12Z"}],"overdue":[],"unavailable":[],"completed":[]}],
              "upcoming":[]
            }
            """));
        var access = new StubAccessService("doit-token");
        var client = new DoItClient(new HttpClient(handler), access);

        var result = await client.GetNowAsync(userId, "2026-07-23", "Europe/Madrid", CancellationToken.None);

        Assert.Equal("me", result.Scope);
        var task = Assert.Single(result.Tasks);
        Assert.Equal("Available", task.State);
        Assert.Equal("Europe/Madrid", task.TimeZoneId);
        Assert.Equal("SpecificUsers", task.AssignmentMode);
        Assert.Equal(["David"], task.AssigneeNames);
        Assert.Equal("Daily", task.RecurrenceType);
        Assert.Equal(new DateTime(2026, 7, 23, 10, 11, 12, DateTimeKind.Utc), task.CompletedAt);
        Assert.Equal("/api/integrations/household/v1/now", handler.Requests.Single().Uri?.AbsolutePath);
        Assert.Contains("date=2026-07-23", handler.Requests.Single().Uri?.Query);
        Assert.Contains("timeZoneId=Europe%2FMadrid", handler.Requests.Single().Uri?.Query);
        AssertAccess(access, userId, "doit", "tasks.read");
    }

    [Fact]
    public async Task JellywatchDashboard_RequestsThreeActivitiesAndBuildsPublicLinks()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {
              "profile":{"displayName":"David","totalSeriesWatching":2,"totalSeriesCompleted":3,"totalMoviesSeen":4,"totalEpisodesSeen":5},
              "activity":[{"eventId":7,"mediaItemId":9,"title":"Arcane","mediaType":"series","eventType":"Finished","timestamp":"2026-07-23T12:00:00Z","posterUrl":"/api/asset/9/poster","tmdbRating":8.7}],
              "upcoming":[{"mediaItemId":9,"seriesId":4,"seriesTitle":"Arcane","seasonNumber":2,"episodeNumber":3,"airDate":"2026-07-24","airTimeUtc":"19:00","batchCount":1,"posterUrl":"/api/asset/9/poster"}]
            }
            """));
        var access = new StubAccessService("jelly-token");
        var client = new JellywatchClient(
            new HttpClient(handler),
            access,
            Options.Create(new HouseholdConnectionSettings { JellywatchOpenUrl = "https://jelly.example" })
        );

        var result = await client.GetDashboardAsync(Guid.NewGuid(), "UTC", CancellationToken.None);

        Assert.Equal(8.7, Assert.Single(result.Activity).TmdbRating);
        Assert.Equal("/modules/media/jellywatch/posters/9?source=activity", result.Activity[0].PosterUrl);
        Assert.Equal("/modules/media/jellywatch/posters/9?source=upcoming", Assert.Single(result.Upcoming).PosterUrl);
        Assert.Contains("activityLimit=20", handler.Requests.Single().Uri?.Query);
        Assert.Contains("upcomingDays=8", handler.Requests.Single().Uri?.Query);
        Assert.Contains("timeZoneId=UTC", handler.Requests.Single().Uri?.Query);
        Assert.Equal("activity.read", access.Requests.Single().Scope);
    }

    [Fact]
    public async Task JellywatchDashboard_FiltersExactSevenDayLocalRange()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var handler = new RecordingHandler(_ => JsonResponse($$"""
            {
              "profile":{"displayName":"David"},"activity":[],
              "upcoming":[
                {"mediaItemId":1,"seriesId":1,"seriesTitle":"Yesterday","seasonNumber":1,"episodeNumber":1,"airDate":"{{today.AddDays(-1):yyyy-MM-dd}}"},
                {"mediaItemId":2,"seriesId":2,"seriesTitle":"Today","seasonNumber":1,"episodeNumber":1,"airDate":"{{today:yyyy-MM-dd}}"},
                {"mediaItemId":3,"seriesId":3,"seriesTitle":"Six","seasonNumber":1,"episodeNumber":1,"airDate":"{{today.AddDays(6):yyyy-MM-dd}}"},
                {"mediaItemId":4,"seriesId":4,"seriesTitle":"Seven","seasonNumber":1,"episodeNumber":1,"airDate":"{{today.AddDays(7):yyyy-MM-dd}}"}
              ]
            }
            """));
        var client = new JellywatchClient(
            new HttpClient(handler),
            new StubAccessService("jelly-token"),
            Options.Create(new HouseholdConnectionSettings { JellywatchOpenUrl = "https://jelly.example" })
        );

        var result = await client.GetDashboardAsync(Guid.NewGuid(), "UTC", CancellationToken.None);

        Assert.Equal(["Today", "Six"], result.Upcoming.Select(item => item.SeriesTitle));
    }

    [Fact]
    public async Task WarcraftQuickStatus_UsesDashboardScopeAndMapsEveryCounter()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {"total":12,"notStarted":1,"pending":2,"inProgress":3,"lastDay":1,"lastWeek":2,"finished":3,"generatedAtUtc":"2026-07-23T12:00:00Z"}
            """));
        var access = new StubAccessService("warcraft-token");
        var client = new WarcraftArchiveClient(new HttpClient(handler), access, Options.Create(new ExternalIntegrationSettings()));

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
            Options.Create(new HouseholdConnectionSettings { BeastVaultOpenUrl = "https://bv.example" }),
            Options.Create(new ExternalIntegrationSettings())
        );

        var result = await client.GetPokemonAsync(Guid.NewGuid(), "pika chu", [3, 8], "home", 0, 24, CancellationToken.None);

        var pokemon = Assert.Single(result.Items);
        Assert.Equal("/modules/pokemon/sprites/25?shiny=false&source=home", pokemon.SpriteUrl);
        Assert.Contains("/other/home/25.png", pokemon.FallbackSpriteUrl);
        Assert.Equal("https://bv.example/pokemon/25", pokemon.OpenUrl);
        var query = handler.Requests.Single().Uri?.Query ?? string.Empty;
        Assert.Contains("search=pika%20chu", query);
        Assert.Contains("tagIds=3", query);
        Assert.Contains("tagIds=8", query);
        Assert.Equal("pokemon.read", access.Requests.Single().Scope);
    }

    [Fact]
    public async Task BeastVaultSearch_DoesNotReturnProviderSuppliedAbsoluteTagUrl()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {"items":[{"id":25,"speciesId":25,"speciesName":"Pikachu","level":20,"tags":[{"id":3,"name":"Team","imagePath":"https://attacker.example/track.png"}]}],"total":1}
            """));
        var client = new BeastVaultClient(
            new HttpClient(handler),
            new StubAccessService("beast-token"),
            Options.Create(new HouseholdConnectionSettings { BeastVaultOpenUrl = "https://bv.example" }),
            Options.Create(new ExternalIntegrationSettings())
        );

        var result = await client.GetPokemonAsync(Guid.NewGuid(), null, [], "home", 0, 24, CancellationToken.None);

        Assert.Null(Assert.Single(Assert.Single(result.Items).Tags).ImageUrl);
    }

    [Fact]
    public async Task UnauthorizedProviderResponse_RefreshesOnce()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler(_ => ++responseNumber == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : JsonResponse("""{"total":0,"notStarted":0,"pending":0,"inProgress":0,"lastDay":0,"lastWeek":0,"finished":0,"generatedAtUtc":"2026-07-23T12:00:00Z"}"""));
        var access = new StubAccessService("old-token", "rotated-token");
        var client = new WarcraftArchiveClient(new HttpClient(handler), access, Options.Create(new ExternalIntegrationSettings()));

        await client.GetQuickStatusAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("old-token", handler.Requests[0].Authorization?.Parameter);
        Assert.Equal("rotated-token", handler.Requests[1].Authorization?.Parameter);
        Assert.False(access.Requests[0].ForceRefresh);
        Assert.True(access.Requests[1].ForceRefresh);
        Assert.Equal("version-1", access.Requests[1].FailedTokenVersion);
    }

    [Fact]
    public async Task DoItOccurrenceAction_UsesNarrowPostEndpointAndWriteScope()
    {
        var occurrenceId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse($$"""
            {"occurrenceId":"{{occurrenceId}}","taskId":"{{Guid.NewGuid()}}","occurrenceDate":"2026-07-23","occurrenceStatus":"Done"}
            """));
        var access = new StubAccessService("doit-write-token");
        var client = new DoItClient(new HttpClient(handler), access);

        var result = await client.CompleteOccurrenceAsync(
            Guid.NewGuid(),
            occurrenceId,
            "2026-07-23",
            "Europe/Madrid",
            CancellationToken.None);

        Assert.Equal("Done", result.OccurrenceStatus);
        Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
        Assert.Equal($"/api/integrations/household/v1/occurrences/{occurrenceId}/complete", handler.Requests.Single().Uri?.AbsolutePath);
        Assert.Equal("tasks.complete", access.Requests.Single().Scope);
    }

    [Theory]
    [InlineData("complete", "Done")]
    [InlineData("undo", "Pending")]
    public async Task DoItAmbiguousOccurrenceAction_ReconcilesSameDateAndTimeZone(string action, string expectedStatus)
    {
        var occurrenceId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var responseNumber = 0;
        var canonicalResponse = $$"""
            {
              "date":"2026-07-22","scope":"me",
              "progress":{"total":1,"done":1,"missed":0,"notApplicable":0,"pending":0},
              "zones":[{"available":[],"overdue":[],"unavailable":[],"completed":[{"occurrenceId":"{{occurrenceId}}","id":"{{taskId}}","title":"Canonical task","scope":"Personal","occurrenceStatus":"{{expectedStatus}}","occurrenceDate":"2026-07-22","assignmentMode":"SingleUser","assigneeIds":[],"assigneeNames":[],"timeZoneId":"Asia/Tokyo","recurrenceType":"Daily"}]}],
              "upcoming":[]
            }
            """;
        var handler = new RecordingHandler(_ => ++responseNumber == 2
            ? throw new TaskCanceledException("provider timeout")
            : JsonResponse(canonicalResponse));
        var client = new DoItClient(new HttpClient(handler), new StubAccessService("doit-token"));
        var userId = Guid.NewGuid();
        await client.GetNowAsync(userId, "2026-07-22", "Asia/Tokyo", CancellationToken.None);

        var result = action == "complete"
            ? await client.CompleteOccurrenceAsync(
                userId, occurrenceId, null, "UTC", CancellationToken.None)
            : await client.UndoOccurrenceAsync(
                userId, occurrenceId, null, "UTC", CancellationToken.None);

        Assert.Equal(expectedStatus, result.OccurrenceStatus);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("date=2026-07-22", handler.Requests[1].Uri?.Query);
        Assert.Contains("timeZoneId=Asia%2FTokyo", handler.Requests[1].Uri?.Query);
        var reconciliation = handler.Requests[2];
        Assert.Equal(HttpMethod.Get, reconciliation.Method);
        Assert.Contains("date=2026-07-22", reconciliation.Uri?.Query);
        Assert.Contains("timeZoneId=Asia%2FTokyo", reconciliation.Uri?.Query);
    }

    [Fact]
    public async Task WarcraftWeekly_MapsReadableStatusAndDifficulty()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            {"total":1,"notStarted":0,"pending":0,"inProgress":1,"lastDay":0,"lastWeek":0,"finished":0,"items":[{"id":"11111111-1111-1111-1111-111111111111","characterName":"Rikku","characterClass":"Mage","contentName":"Raid","expansion":"The War Within","difficulty":4,"status":2,"updatedAt":"2026-07-23T12:00:00Z"}]}
            """));
        var access = new StubAccessService("warcraft-token");
        var client = new WarcraftArchiveClient(new HttpClient(handler), access, Options.Create(new ExternalIntegrationSettings()));

        var result = await client.GetWeeklyAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, result.Summary.CompletionPercent);
        var item = Assert.Single(result.Items);
        Assert.Equal("Heroic", item.Difficulty);
        Assert.Equal("In progress", item.Status);
        Assert.Equal("/dashboard/weekly", handler.Requests.Single().Uri?.AbsolutePath);
    }

    [Fact]
    public async Task WarcraftStatus_UsesPlannedNarrowRouteAndWriteScope()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse($$"""
            {"id":"{{id}}","characterName":"Rikku","contentName":"Raid","expansion":"TWW","difficulty":4,"status":5,"updatedAt":"2026-07-23T12:00:00Z"}
            """));
        var access = new StubAccessService("warcraft-token");
        var client = new WarcraftArchiveClient(new HttpClient(handler), access, Options.Create(new ExternalIntegrationSettings()));

        var result = await client.UpdateTrackingStatusAsync(Guid.NewGuid(), id, "Finished", CancellationToken.None);

        Assert.Equal("Finished", result.Status);
        Assert.Equal(HttpMethod.Patch, handler.Requests.Single().Method);
        Assert.Equal($"/api/integrations/household/v1/trackings/{id}/status", handler.Requests.Single().Uri?.AbsolutePath);
        Assert.Equal("tracking.status.write", access.Requests.Single().Scope);
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
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, request.Headers.Authorization));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? Uri,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization
    );
}
