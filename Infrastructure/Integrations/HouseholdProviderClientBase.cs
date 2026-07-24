using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;

namespace Household.Api.Infrastructure.Integrations;

public abstract class HouseholdProviderClientBase
{
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

    private async Task<T?> SendAsync<T>(
        Guid userId,
        string requiredScope,
        HttpMethod method,
        string path,
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

        using var request = new HttpRequestMessage(method, $"{access.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
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
                    failedTokenVersion: access.TokenVersion
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

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" && string.IsNullOrEmpty(absolute.UserInfo)
                ? absolute.ToString()
                : null;

        return $"{publicBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
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
