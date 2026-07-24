using Household.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Household.Api.Tests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task SafeInboundId_IsReturnedAndStoredForOutboundPropagation()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = "request_123.safe-id";
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance
        );

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.Equal("request_123.safe-id", context.TraceIdentifier);
        Assert.Equal("request_123.safe-id", context.Response.Headers[CorrelationId.HeaderName]);
        Assert.Equal("request_123.safe-id", CorrelationId.Get(context));
    }

    [Theory]
    [InlineData("contains space")]
    [InlineData("line\r\nbreak")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task UnsafeInboundId_IsReplaced(string unsafeValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = unsafeValue;
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance
        );

        await middleware.InvokeAsync(context);

        Assert.NotEqual(unsafeValue, context.TraceIdentifier);
        Assert.Matches("^[a-f0-9]{32}$", context.TraceIdentifier);
    }

    [Fact]
    public async Task OutboundHandler_PropagatesValidatedRequestId()
    {
        var context = new DefaultHttpContext();
        context.Items[CorrelationId.ItemKey] = "request-123";
        string? observed = null;
        var handler = new CorrelationIdHandler(new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = new CaptureHandler(request =>
                observed = request.Headers.GetValues(CorrelationId.HeaderName).Single())
        };

        await new HttpClient(handler).GetAsync("https://provider.example/test");

        Assert.Equal("request-123", observed);
    }

    private sealed class CaptureHandler(Action<HttpRequestMessage> capture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            capture(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
