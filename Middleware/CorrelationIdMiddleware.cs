using Microsoft.Extensions.Primitives;

namespace Household.Api.Middleware;

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";
    public static readonly object ItemKey = new();
    private const int MaxLength = 64;

    public static string Create(StringValues values)
    {
        if (values.Count == 1 && IsSafe(values[0]))
            return values[0]!;

        return Guid.NewGuid().ToString("N");
    }

    public static string? Get(HttpContext? context) =>
        context?.Items.TryGetValue(ItemKey, out var value) == true ? value as string : null;

    private static bool IsSafe(string? value) =>
        value is { Length: > 0 and <= MaxLength }
        && value.All(character =>
            character is >= 'a' and <= 'z'
            || character is >= 'A' and <= 'Z'
            || character is >= '0' and <= '9'
            || character is '-' or '_' or '.');
}

public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationId.Create(context.Request.Headers[CorrelationId.HeaderName]);
        context.Items[CorrelationId.ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            await next(context);
    }
}

public sealed class CorrelationIdHandler(IHttpContextAccessor contextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var correlationId = CorrelationId.Get(contextAccessor.HttpContext) ?? Guid.NewGuid().ToString("N");
        request.Headers.Remove(CorrelationId.HeaderName);
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, correlationId);
        return base.SendAsync(request, cancellationToken);
    }
}
