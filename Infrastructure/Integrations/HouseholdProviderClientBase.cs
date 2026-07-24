using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;

namespace Household.Api.Infrastructure.Integrations;

public abstract class HouseholdProviderClientBase
{
    protected sealed record ProviderFile(byte[] Content, string ContentType, string? FileName);
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IHouseholdProviderAccessService _connectionAccess;
    private readonly string _providerId;
    private readonly string _displayName;

    protected HouseholdProviderClientBase(
        HttpClient httpClient,
        IHouseholdProviderAccessService connectionAccess,
        string providerId,
        string displayName
    )
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _connectionAccess = connectionAccess;
        _providerId = providerId;
        _displayName = displayName;
    }

    protected async Task<T> GetRequiredAsync<T>(
        Guid userId,
        string requiredScope,
        string path,
        CancellationToken cancellationToken
    ) =>
        await SendAsync<T>(userId, requiredScope, HttpMethod.Get, path, cancellationToken)
        ?? throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} returned an empty response.");

    protected async Task<T> PostRequiredAsync<T>(
        Guid userId,
        string requiredScope,
        string path,
        CancellationToken cancellationToken
    ) =>
        await SendAsync<T>(userId, requiredScope, HttpMethod.Post, path, cancellationToken)
        ?? throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} returned an empty response.");

    protected async Task<T> PatchRequiredAsync<T>(
        Guid userId,
        string requiredScope,
        string path,
        object body,
        CancellationToken cancellationToken
    ) => await SendAsync<T>(userId, requiredScope, HttpMethod.Patch, path, cancellationToken, body: body)
        ?? throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} returned an empty response.");

    protected async Task<ProviderFile?> DownloadAsync(
        Guid userId,
        string requiredScope,
        string path,
        int maxBytes,
        CancellationToken cancellationToken,
        bool retrying = false,
        string? failedTokenVersion = null
    )
    {
        var access = await _connectionAccess.GetAccessAsync(
            userId,
            _providerId,
            requiredScope,
            retrying,
            failedTokenVersion,
            cancellationToken
        );
        if (access.Status != HouseholdProviderAccessStatus.Success || access.AccessToken is null || access.BaseUrl is null)
            throw ToGatewayException(access.Status);

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(access.BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.BadGateway,
                $"{_displayName} asset request timed out.",
                "provider_timeout"
            );
        }
        catch (HttpRequestException)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.BadGateway,
                $"{_displayName} is unavailable.",
                "provider_unavailable"
            );
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && !retrying)
                return await DownloadAsync(
                    userId,
                    requiredScope,
                    path,
                    maxBytes,
                    cancellationToken,
                    retrying: true,
                    failedTokenVersion: access.TokenVersion
                );
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new IntegrationGatewayException(
                    HttpStatusCode.Conflict,
                    $"Reconnect {_displayName} to continue.",
                    "provider_reconnect_required"
                );
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new IntegrationGatewayException(HttpStatusCode.Forbidden, $"{_displayName} permission is missing.", "permission_missing");
            if (!response.IsSuccessStatusCode)
                throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} asset request failed.", "provider_asset_failed");
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} asset is too large.", "provider_asset_too_large");

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                while (true)
                {
                    var read = await stream.ReadAsync(chunk, cancellationToken);
                    if (read == 0) break;
                    if (buffer.Length + read > maxBytes)
                        throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} asset is too large.", "provider_asset_too_large");
                    buffer.Write(chunk, 0, read);
                }
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                if (contentType.Length > 100 || contentType.Any(char.IsControl)) contentType = "application/octet-stream";
                var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                return new ProviderFile(buffer.ToArray(), contentType, fileName);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IntegrationGatewayException(
                    HttpStatusCode.BadGateway,
                    $"{_displayName} asset request timed out.",
                    "provider_timeout"
                );
            }
            catch (HttpRequestException)
            {
                throw new IntegrationGatewayException(
                    HttpStatusCode.BadGateway,
                    $"{_displayName} returned an invalid asset response.",
                    "invalid_provider_response"
                );
            }
        }
    }

    private async Task<T?> SendAsync<T>(
        Guid userId,
        string requiredScope,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        bool retrying = false,
        string? failedTokenVersion = null,
        object? body = null
    )
    {
        var access = await _connectionAccess.GetAccessAsync(
            userId,
            _providerId,
            requiredScope,
            retrying,
            failedTokenVersion,
            cancellationToken
        );
        if (access.Status != HouseholdProviderAccessStatus.Success || access.AccessToken is null || access.BaseUrl is null)
            throw ToGatewayException(access.Status);

        using var request = new HttpRequestMessage(method, BuildRequestUri(access.BaseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && method != HttpMethod.Get)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                $"{_displayName} may have applied the change; canonical state must be checked.",
                "ambiguous_timeout",
                reconcilable: true
            );
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        )
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} is unavailable.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && !retrying)
                return await SendAsync<T>(
                    userId,
                    requiredScope,
                    method,
                    path,
                    cancellationToken,
                    retrying: true,
                    failedTokenVersion: access.TokenVersion,
                    body: body
                );
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new IntegrationGatewayException(HttpStatusCode.Forbidden, $"{_displayName} permission is missing.");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new IntegrationGatewayException(HttpStatusCode.Conflict, $"Reconnect {_displayName} to continue.");
            if (!response.IsSuccessStatusCode)
                throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} request failed.");

            try
            {
                await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken);
                return await JsonSerializer.DeserializeAsync<T>(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    JsonOptions,
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is JsonException or HttpRequestException)
            {
                throw new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} returned an invalid response.");
            }
        }
    }

    protected static string BuildQuery(IEnumerable<KeyValuePair<string, string?>> values)
    {
        var pairs = values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}")
            .ToList();
        return pairs.Count == 0 ? string.Empty : $"?{string.Join("&", pairs)}";
    }

    private static Uri BuildRequestUri(string baseUrl, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out _)
            || path.StartsWith("//", StringComparison.Ordinal)
            || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || !Uri.TryCreate(origin, "/" + path.TrimStart('/'), out var combined)
            || !string.Equals(origin.Scheme, combined.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Host, combined.Host, StringComparison.OrdinalIgnoreCase)
            || origin.Port != combined.Port)
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Provider path is invalid.", "invalid_provider_path");
        return combined;
    }

    protected static string? BuildPublicUrl(string? publicBaseUrl, string? path)
    {
        if (
            string.IsNullOrWhiteSpace(publicBaseUrl)
            || !Uri.TryCreate(publicBaseUrl.TrimEnd('/'), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || string.IsNullOrWhiteSpace(path)
        )
            return null;

        // Provider-returned absolute URLs are not trusted. Keeping links on the
        // configured public origin prevents a compromised provider from turning
        // Household DTOs into arbitrary external links.
        if (Uri.TryCreate(path, UriKind.Absolute, out _)) return null;

        var normalizedBase = publicBaseUrl.TrimEnd('/');
        if (!Uri.TryCreate(normalizedBase + "/", UriKind.Absolute, out var origin)
            || !Uri.TryCreate(origin, path.TrimStart('/'), out var combined)
            || !string.Equals(origin.Scheme, combined.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Host, combined.Host, StringComparison.OrdinalIgnoreCase)
            || origin.Port != combined.Port)
            return null;
        return combined.ToString();
    }

    private IntegrationGatewayException ToGatewayException(HouseholdProviderAccessStatus status) =>
        status switch
        {
            HouseholdProviderAccessStatus.MissingScope =>
                new IntegrationGatewayException(HttpStatusCode.Forbidden, $"{_displayName} permission is missing."),
            HouseholdProviderAccessStatus.ProviderUnavailable =>
                new IntegrationGatewayException(HttpStatusCode.BadGateway, $"{_displayName} is unavailable."),
            _ => new IntegrationGatewayException(HttpStatusCode.Conflict, $"Connect {_displayName} to continue."),
        };
}
