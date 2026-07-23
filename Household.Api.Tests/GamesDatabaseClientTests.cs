using System.Net;
using System.Text;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.Infrastructure.Integrations.GamesDatabase;
using Microsoft.Extensions.Options;

namespace Household.Api.Tests;

public class GamesDatabaseClientTests
{
    [Fact]
    public async Task List_UsesTheCurrentUsersIntegrationToken()
    {
        var userId = Guid.NewGuid();
        var handler = new RecordingHandler(
            _ => JsonResponse("""
                {"data":[{"id":7,"statusId":2,"name":"Test Game","statusName":"Playing","cover":"/game-images/test.jpg"}],"totalCount":1,"page":1,"pageSize":24,"totalPages":1}
                """)
        );
        var access = new StubAccessService("gdi_user_token");
        var client = CreateClient(handler, access);

        var result = await client.GetGamesAsync(userId, null, null, 1, 24, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("Test Game", result.Items[0].Name);
        Assert.Equal("https://games.example/game-images/test.jpg", result.Items[0].Cover);
        Assert.Equal(userId, access.Requests.Single().UserId);
        Assert.Equal("games.read", access.Requests.Single().Scope);
        Assert.Equal("Bearer", handler.Requests.Single().Authorization?.Scheme);
        Assert.Equal("gdi_user_token", handler.Requests.Single().Authorization?.Parameter);
    }

    [Fact]
    public async Task UpdateStatus_UsesNarrowPatchEndpointWithoutASecondRequest()
    {
        var handler = new RecordingHandler(
            _ => JsonResponse("""{"id":7,"statusId":3,"name":"Test Game","statusName":"Finished"}""")
        );
        var access = new StubAccessService("gdi_write_token");
        var client = CreateClient(handler, access);

        var result = await client.UpdateStatusAsync(Guid.NewGuid(), 7, 3, CancellationToken.None);

        Assert.NotNull(result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("/api/games/7/status", request.RequestUri?.AbsolutePath);
        Assert.Contains("\"statusId\":3", request.Body);
        Assert.Equal("games.status.write", access.Requests.Single().Scope);
    }

    [Fact]
    public async Task UnauthorizedResponse_RefreshesOnceAndRetriesWithTheRotatedToken()
    {
        var responseNumber = 0;
        var handler = new RecordingHandler(
            _ => ++responseNumber == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : JsonResponse("[]")
        );
        var access = new StubAccessService("gdi_old", "gdi_rotated");
        var client = CreateClient(handler, access);

        await client.GetStatusesAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("gdi_old", handler.Requests[0].Authorization?.Parameter);
        Assert.Equal("gdi_rotated", handler.Requests[1].Authorization?.Parameter);
        Assert.Collection(
            access.Requests,
            request => Assert.False(request.ForceRefresh),
            request =>
            {
                Assert.True(request.ForceRefresh);
                Assert.Equal("version-1", request.FailedTokenVersion);
            }
        );
    }

    private static GamesDatabaseClient CreateClient(RecordingHandler handler, StubAccessService access) =>
        new(
            new HttpClient(handler),
            Options.Create(new GamesDatabaseSettings { OpenUrl = "https://games.example", TimeoutSeconds = 15 }),
            access
        );

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
            Requests.Add(new AccessRequest(userId, requiredScope, forceRefresh, failedTokenVersion));
            var index = Math.Min(_requestIndex++, tokens.Length - 1);
            return Task.FromResult(
                new HouseholdProviderAccessResult(
                    HouseholdProviderAccessStatus.Success,
                    tokens[index],
                    "https://games-api.example",
                    $"version-{index + 1}"
                )
            );
        }
    }

    private sealed record AccessRequest(Guid UserId, string Scope, bool ForceRefresh, string? FailedTokenVersion);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, request.Headers.Authorization, body));
            return responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization,
        string Body
    );
}
