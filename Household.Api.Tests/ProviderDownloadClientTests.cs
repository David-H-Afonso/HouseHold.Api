using System.Net;
using System.Net.Http.Headers;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;
using Household.Api.Infrastructure.Integrations;

namespace Household.Api.Tests;

public sealed class ProviderDownloadClientTests
{
    [Fact]
    public async Task UnauthorizedDownload_RefreshesExactlyOnceAndReturnsFile()
    {
        var responseNumber = 0;
        var handler = new StubHandler(_ => ++responseNumber == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : FileResponse([1, 2, 3]));
        var access = new StubAccessService("old-token", "new-token");
        var client = new DownloadClient(new HttpClient(handler), access);

        var result = await client.DownloadAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3], result);
        Assert.Equal(["old-token", "new-token"], handler.Tokens);
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

    [Fact]
    public async Task SecondUnauthorizedDownload_DoesNotRetryAgain()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var access = new StubAccessService("old-token", "new-token", "never-used");
        var client = new DownloadClient(new HttpClient(handler), access);

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => client.DownloadAsync(CancellationToken.None));

        Assert.Equal("provider_reconnect_required", exception.Code);
        Assert.Equal(2, handler.Tokens.Count);
        Assert.Equal(2, access.Requests.Count);
    }

    [Fact]
    public async Task Timeout_UsesSafeGatewayTaxonomy()
    {
        var client = new DownloadClient(
            new HttpClient(new StubHandler(_ => throw new TaskCanceledException("provider details"))),
            new StubAccessService("token")
        );

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => client.DownloadAsync(CancellationToken.None));

        Assert.Equal("provider_timeout", exception.Code);
        Assert.DoesNotContain("provider details", exception.Message);
    }

    [Fact]
    public async Task TransportFailure_UsesSafeGatewayTaxonomy()
    {
        var client = new DownloadClient(
            new HttpClient(new StubHandler(_ => throw new HttpRequestException("private host details"))),
            new StubAccessService("token")
        );

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => client.DownloadAsync(CancellationToken.None));

        Assert.Equal("provider_unavailable", exception.Code);
        Assert.DoesNotContain("private host details", exception.Message);
    }

    [Fact]
    public async Task OversizedChunkedContent_IsRejectedWithoutReturningPartialFile()
    {
        var client = new DownloadClient(
            new HttpClient(new StubHandler(_ => FileResponse(new byte[33], contentLength: false))),
            new StubAccessService("token"),
            maxBytes: 32
        );

        var exception = await Assert.ThrowsAsync<IntegrationGatewayException>(() => client.DownloadAsync(CancellationToken.None));

        Assert.Equal("provider_asset_too_large", exception.Code);
    }

    private static HttpResponseMessage FileResponse(byte[] bytes, bool contentLength = true)
    {
        HttpContent content = contentLength
            ? new ByteArrayContent(bytes)
            : new StreamContent(new MemoryStream(bytes));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (!contentLength) content.Headers.ContentLength = null;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class DownloadClient(
        HttpClient httpClient,
        IHouseholdProviderAccessService access,
        int maxBytes = 1024
    ) : HouseholdProviderClientBase(httpClient, access, "test", "Test Provider")
    {
        public async Task<byte[]?> DownloadAsync(CancellationToken cancellationToken) =>
            (await base.DownloadAsync(Guid.NewGuid(), "files.read", "/file", maxBytes, cancellationToken))?.Content;

    }

    private sealed class StubAccessService(params string[] tokens) : IHouseholdProviderAccessService
    {
        private int _index;
        public List<(bool ForceRefresh, string? FailedTokenVersion)> Requests { get; } = [];

        public Task<HouseholdProviderAccessResult> GetAccessAsync(
            Guid userId,
            string providerId,
            string requiredScope,
            bool forceRefresh,
            string? failedTokenVersion,
            CancellationToken cancellationToken
        )
        {
            Requests.Add((forceRefresh, failedTokenVersion));
            var index = Math.Min(_index++, tokens.Length - 1);
            return Task.FromResult(new HouseholdProviderAccessResult(
                HouseholdProviderAccessStatus.Success,
                tokens[index],
                "https://provider.example",
                $"version-{index + 1}"
            ));
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<string?> Tokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Tokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(responseFactory(request));
        }
    }
}
