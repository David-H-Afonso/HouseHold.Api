using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Application.Services;

public enum HouseholdAuthorizeResultKind
{
    Success,
    UnknownProvider,
    NotConfigured,
}

public record HouseholdAuthorizeResult(HouseholdAuthorizeResultKind Kind, string? AuthorizationUrl = null);

public record HouseholdCallbackResult(string? RedirectUrl, bool CanRedirect);

public enum HouseholdDisconnectResult
{
    Success,
    NotFound,
    UpstreamFailure,
}

public class HouseholdConsumerConnectionService
{
    private const int MaxProviderResponseBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16,
    };

    private readonly AppDbContext _db;
    private readonly HouseholdProviderRegistry _providers;
    private readonly HouseholdConnectionCoordinator _coordinator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _tokenProtector;
    private readonly IDataProtector _pkceProtector;

    public HouseholdConsumerConnectionService(
        AppDbContext db,
        HouseholdProviderRegistry providers,
        HouseholdConnectionCoordinator coordinator,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider
    )
    {
        _db = db;
        _providers = providers;
        _coordinator = coordinator;
        _httpClientFactory = httpClientFactory;
        _tokenProtector = dataProtectionProvider.CreateProtector("Household.ConsumerConnections.Tokens.v1");
        _pkceProtector = dataProtectionProvider.CreateProtector("Household.ConsumerConnections.Pkce.v1");
    }

    public async Task<IReadOnlyList<HouseholdConnectionDto>> GetConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken
    )
    {
        var connections = await _db
            .HouseholdConsumerConnections.AsNoTracking()
            .Where(connection => connection.UserId == userId)
            .ToDictionaryAsync(connection => connection.Provider, StringComparer.Ordinal, cancellationToken);

        return _providers
            .GetAll()
            .Select(provider =>
                connections.TryGetValue(provider.Id, out var connection)
                    ? ToDto(provider, connection)
                    : ToDisconnectedDto(provider)
            )
            .ToList();
    }

    public async Task<HouseholdAuthorizeResult> AuthorizeAsync(
        Guid userId,
        string providerId,
        CancellationToken cancellationToken
    )
    {
        if (!_providers.TryGet(providerId, out var provider))
            return new HouseholdAuthorizeResult(HouseholdAuthorizeResultKind.UnknownProvider);
        if (!provider.Configured || provider.RedirectUri is null)
            return new HouseholdAuthorizeResult(HouseholdAuthorizeResultKind.NotConfigured);

        var values = HouseholdConnectionCrypto.CreatePkceValues();
        var authorizationUrl = _providers.BuildAuthorizationUrl(provider, values.State, values.Challenge);
        if (authorizationUrl is null)
            return new HouseholdAuthorizeResult(HouseholdAuthorizeResultKind.NotConfigured);

        var now = DateTime.UtcNow;
        _db.HouseholdAuthorizationAttempts.Add(
            new HouseholdAuthorizationAttempt
            {
                UserId = userId,
                Provider = provider.Id,
                StateHash = values.StateHash,
                ProtectedCodeVerifier = _pkceProtector.Protect(values.Verifier),
                RedirectUri = provider.RedirectUri,
                RequestedScopes = JoinScopes(provider.Scopes),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(5),
            }
        );
        await _db.SaveChangesAsync(cancellationToken);

        return new HouseholdAuthorizeResult(HouseholdAuthorizeResultKind.Success, authorizationUrl);
    }

    public async Task<HouseholdCallbackResult> HandleCallbackAsync(
        string providerId,
        string? code,
        string? state,
        string? oauthError,
        CancellationToken cancellationToken
    )
    {
        if (!_providers.TryGet(providerId, out var provider))
            return CallbackFailure(providerId, "unknown_provider");
        if (!provider.Configured || provider.RedirectUri is null || provider.BaseUrl is null)
            return CallbackFailure(providerId, "provider_not_configured");
        if (string.IsNullOrEmpty(state) || state.Length > 128)
            return CallbackFailure(providerId, "invalid_state");
        if (!string.IsNullOrEmpty(code) && code.Length > 2048)
            return CallbackFailure(providerId, "invalid_response");
        if (!string.IsNullOrEmpty(oauthError) && oauthError.Length > 200)
            return CallbackFailure(providerId, "authorization_failed");

        var stateHash = HouseholdConnectionCrypto.HashState(state);
        var attempt = await _db.HouseholdAuthorizationAttempts.SingleOrDefaultAsync(
            item => item.StateHash == stateHash && item.Provider == provider.Id,
            cancellationToken
        );
        var now = DateTime.UtcNow;
        if (attempt is null || attempt.ConsumedAt is not null || attempt.ExpiresAt <= now)
            return CallbackFailure(providerId, attempt?.ExpiresAt <= now ? "authorization_expired" : "invalid_state");

        var consumed = await _db
            .HouseholdAuthorizationAttempts.Where(item =>
                item.Id == attempt.Id && item.ConsumedAt == null && item.ExpiresAt > now
            )
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now), cancellationToken);
        if (consumed != 1)
            return CallbackFailure(providerId, "invalid_state");

        if (!string.IsNullOrEmpty(oauthError))
            return CallbackFailure(
                providerId,
                string.Equals(oauthError, "access_denied", StringComparison.Ordinal) ? "access_denied" : "authorization_failed"
            );
        if (string.IsNullOrEmpty(code))
            return CallbackFailure(providerId, "invalid_response");
        if (!string.Equals(attempt.RedirectUri, provider.RedirectUri, StringComparison.Ordinal))
            return CallbackFailure(providerId, "invalid_attempt");

        string verifier;
        try
        {
            verifier = _pkceProtector.Unprotect(attempt.ProtectedCodeVerifier);
        }
        catch (CryptographicException)
        {
            return CallbackFailure(providerId, "invalid_attempt");
        }

        var tokenResponse = await SendTokenRequestAsync(
            provider,
            new
            {
                grantType = "authorization_code",
                clientId = _providers.ClientId,
                redirectUri = attempt.RedirectUri,
                code,
                codeVerifier = verifier,
            },
            cancellationToken
        );
        if (!tokenResponse.IsSuccess || tokenResponse.Value is null)
            return CallbackFailure(providerId, "token_exchange_failed");

        var requestedScopes = SplitScopes(attempt.RequestedScopes);
        var validated = ValidateTokenResponse(provider, tokenResponse.Value, requestedScopes, requireExactScopes: false);
        if (validated is null)
            return CallbackFailure(providerId, "invalid_token_response");

        var gate = _coordinator.Get(attempt.UserId, provider.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var connection = await _db.HouseholdConsumerConnections.SingleOrDefaultAsync(
                item => item.UserId == attempt.UserId && item.Provider == provider.Id,
                cancellationToken
            );
            connection ??= new HouseholdConsumerConnection { UserId = attempt.UserId, Provider = provider.Id };
            if (_db.Entry(connection).State == EntityState.Detached)
                _db.HouseholdConsumerConnections.Add(connection);

            ApplyTokens(connection, validated, now);
            connection.Status = HouseholdConnectionStatus.Connected;
            connection.LastError = null;
            connection.ConnectedAt = now;
            connection.LastValidatedAt = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        var redirectUrl = _providers.BuildCompletionUrl(provider.Id, success: true);
        return new HouseholdCallbackResult(redirectUrl, redirectUrl is not null);
    }

    public async Task<HouseholdConnectionDto?> TestAsync(
        Guid userId,
        string providerId,
        CancellationToken cancellationToken
    )
    {
        if (!_providers.TryGet(providerId, out var provider))
            return null;

        var connection = await _db.HouseholdConsumerConnections.SingleOrDefaultAsync(
            item => item.UserId == userId && item.Provider == provider.Id,
            cancellationToken
        );
        if (connection is null)
            return null;
        if (!provider.Configured || provider.BaseUrl is null)
        {
            await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "provider_not_configured", cancellationToken);
            return ToDto(provider, connection);
        }

        var accessToken = await GetAccessTokenAsync(
            connection,
            provider,
            forceRefresh: false,
            failedProtectedAccessToken: null,
            cancellationToken
        );
        if (accessToken is null)
            return ToDto(provider, connection);
        var failedProtectedAccessToken = connection.ProtectedAccessToken;

        var meResponse = await SendMeAsync(provider, accessToken, cancellationToken);
        if (meResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            accessToken = await GetAccessTokenAsync(
                connection,
                provider,
                forceRefresh: true,
                failedProtectedAccessToken,
                cancellationToken
            );
            if (accessToken is null)
                return ToDto(provider, connection);
            meResponse = await SendMeAsync(provider, accessToken, cancellationToken);
        }

        if (!meResponse.IsSuccess || meResponse.Value is null)
        {
            await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "provider_unavailable", cancellationToken);
            return ToDto(provider, connection);
        }

        var expectedScopes = SplitScopes(connection.GrantedScopes);
        if (!ValidateIdentity(provider, connection, meResponse.Value, expectedScopes))
        {
            await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "identity_validation_failed", cancellationToken);
            return ToDto(provider, connection);
        }

        connection.AccountDisplayName = meResponse.Value.Account!.DisplayName!.Trim();
        connection.Status = HouseholdConnectionStatus.Connected;
        connection.LastError = null;
        connection.LastValidatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(provider, connection);
    }

    public async Task<HouseholdDisconnectResult> DisconnectAsync(
        Guid userId,
        string providerId,
        CancellationToken cancellationToken
    )
    {
        if (!_providers.TryGet(providerId, out var provider))
            return HouseholdDisconnectResult.NotFound;

        var gate = _coordinator.Get(userId, provider.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var connection = await _db.HouseholdConsumerConnections.SingleOrDefaultAsync(
                item => item.UserId == userId && item.Provider == provider.Id,
                cancellationToken
            );
            if (connection is null)
                return HouseholdDisconnectResult.NotFound;
            if (!provider.Configured || provider.BaseUrl is null)
            {
                await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "provider_not_configured", cancellationToken);
                return HouseholdDisconnectResult.UpstreamFailure;
            }

            string refreshToken;
            try
            {
                refreshToken = _tokenProtector.Unprotect(connection.ProtectedRefreshToken);
            }
            catch (CryptographicException)
            {
                await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "token_unavailable", cancellationToken);
                return HouseholdDisconnectResult.UpstreamFailure;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{provider.BaseUrl}/api/integrations/household/v1/revoke"
            )
            {
                Content = JsonContent.Create(new { token = refreshToken, tokenTypeHint = "refresh_token" }),
            };

            try
            {
                using var response = await _httpClientFactory
                    .CreateClient("HouseholdProviders")
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "revocation_failed", cancellationToken);
                    return HouseholdDisconnectResult.UpstreamFailure;
                }
            }
            catch (HttpRequestException)
            {
                await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "provider_unavailable", cancellationToken);
                return HouseholdDisconnectResult.UpstreamFailure;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "provider_unavailable", cancellationToken);
                return HouseholdDisconnectResult.UpstreamFailure;
            }

            _db.HouseholdConsumerConnections.Remove(connection);
            await _db.SaveChangesAsync(cancellationToken);
            return HouseholdDisconnectResult.Success;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string?> GetAccessTokenAsync(
        HouseholdConsumerConnection connection,
        HouseholdProviderDefinition provider,
        bool forceRefresh,
        string? failedProtectedAccessToken,
        CancellationToken cancellationToken
    )
    {
        if (!forceRefresh && connection.AccessTokenExpiresAt > DateTime.UtcNow.AddSeconds(30))
        {
            var storedToken = TryUnprotectAccessToken(connection);
            if (storedToken is null)
                await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "token_unavailable", cancellationToken);
            return storedToken;
        }

        var gate = _coordinator.Get(connection.UserId, connection.Provider);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await _db.Entry(connection).ReloadAsync(cancellationToken);
            if (
                forceRefresh
                && failedProtectedAccessToken is not null
                && !string.Equals(connection.ProtectedAccessToken, failedProtectedAccessToken, StringComparison.Ordinal)
                && connection.AccessTokenExpiresAt > DateTime.UtcNow.AddSeconds(30)
            )
            {
                var storedToken = TryUnprotectAccessToken(connection);
                if (storedToken is null)
                    await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "token_unavailable", cancellationToken);
                return storedToken;
            }
            if (!forceRefresh && connection.AccessTokenExpiresAt > DateTime.UtcNow.AddSeconds(30))
            {
                var storedToken = TryUnprotectAccessToken(connection);
                if (storedToken is null)
                    await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "token_unavailable", cancellationToken);
                return storedToken;
            }
            if (connection.RefreshTokenExpiresAt <= DateTime.UtcNow)
            {
                await SetFailureAsync(connection, HouseholdConnectionStatus.Expired, "refresh_token_expired", cancellationToken);
                return null;
            }

            string refreshToken;
            try
            {
                refreshToken = _tokenProtector.Unprotect(connection.ProtectedRefreshToken);
            }
            catch (CryptographicException)
            {
                await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "token_unavailable", cancellationToken);
                return null;
            }

            var response = await SendTokenRequestAsync(
                provider,
                new { grantType = "refresh_token", clientId = _providers.ClientId, refreshToken },
                cancellationToken
            );
            if (!response.IsSuccess || response.Value is null)
            {
                var expired = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized;
                await SetFailureAsync(
                    connection,
                    expired ? HouseholdConnectionStatus.Expired : HouseholdConnectionStatus.Error,
                    expired ? "refresh_token_expired" : "provider_unavailable",
                    cancellationToken
                );
                return null;
            }

            var currentScopes = SplitScopes(connection.GrantedScopes);
            var validated = ValidateTokenResponse(provider, response.Value, currentScopes, requireExactScopes: true);
            if (
                validated is null
                || !string.Equals(validated.ConnectionId, connection.SourceConnectionId, StringComparison.Ordinal)
                || !string.Equals(validated.AccountId, connection.AccountId, StringComparison.Ordinal)
            )
            {
                await SetFailureAsync(connection, HouseholdConnectionStatus.Error, "identity_validation_failed", cancellationToken);
                return null;
            }

            ApplyTokens(connection, validated, DateTime.UtcNow);
            connection.Status = HouseholdConnectionStatus.Connected;
            connection.LastError = null;
            await _db.SaveChangesAsync(cancellationToken);
            return validated.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProviderResponse<TokenResponse>> SendTokenRequestAsync(
        HouseholdProviderDefinition provider,
        object payload,
        CancellationToken cancellationToken
    )
    {
        if (provider.BaseUrl is null)
            return ProviderResponse<TokenResponse>.Failure(null);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{provider.BaseUrl}/api/integrations/household/v1/token"
        )
        {
            Content = JsonContent.Create(payload),
        };
        return await SendJsonAsync<TokenResponse>(request, cancellationToken);
    }

    private async Task<ProviderResponse<MeResponse>> SendMeAsync(
        HouseholdProviderDefinition provider,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{provider.BaseUrl}/api/integrations/household/v1/me"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendJsonAsync<MeResponse>(request, cancellationToken);
    }

    private async Task<ProviderResponse<T>> SendJsonAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var response = await _httpClientFactory
                .CreateClient("HouseholdProviders")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ProviderResponse<T>.Failure(response.StatusCode);
            if (response.Content.Headers.ContentLength > MaxProviderResponseBytes)
                return ProviderResponse<T>.Failure(response.StatusCode);

            await response.Content.LoadIntoBufferAsync(MaxProviderResponseBytes, cancellationToken);
            var value = await JsonSerializer.DeserializeAsync<T>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                JsonOptions,
                cancellationToken
            );
            return value is null
                ? ProviderResponse<T>.Failure(response.StatusCode)
                : ProviderResponse<T>.Success(response.StatusCode, value);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        )
        {
            return ProviderResponse<T>.Failure(null);
        }
    }

    private ValidatedToken? ValidateTokenResponse(
        HouseholdProviderDefinition provider,
        TokenResponse response,
        IReadOnlySet<string> expectedScopes,
        bool requireExactScopes
    )
    {
        if (
            !string.Equals(response.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(response.AccessToken)
            || response.AccessToken.Length > 16384
            || string.IsNullOrWhiteSpace(response.RefreshToken)
            || response.RefreshToken.Length > 16384
            || response.ExpiresIn is <= 0 or > 86400
            || response.RefreshExpiresIn is <= 0 or > 7776000
            || string.IsNullOrWhiteSpace(response.ConnectionId)
            || response.ConnectionId.Length > 200
            || response.Account is null
            || string.IsNullOrWhiteSpace(response.Account.Id)
            || response.Account.Id.Length > 500
            || string.IsNullOrWhiteSpace(response.Account.DisplayName)
            || response.Account.DisplayName.Length > 200
        )
            return null;

        var scopes = SplitScopes(response.Scope);
        var allowlist = provider.Scopes.ToHashSet(StringComparer.Ordinal);
        if (
            scopes.Count == 0
            || !scopes.IsSubsetOf(allowlist)
            || !scopes.IsSubsetOf(expectedScopes)
            || (requireExactScopes && !scopes.SetEquals(expectedScopes))
        )
            return null;

        return new ValidatedToken(
            response.AccessToken,
            response.RefreshToken,
            response.ExpiresIn,
            response.RefreshExpiresIn,
            response.ConnectionId.Trim(),
            response.Account.Id.Trim(),
            response.Account.DisplayName.Trim(),
            scopes
        );
    }

    private static bool ValidateIdentity(
        HouseholdProviderDefinition provider,
        HouseholdConsumerConnection connection,
        MeResponse response,
        IReadOnlySet<string> expectedScopes
    )
    {
        if (
            (!string.IsNullOrEmpty(response.Provider) && !string.Equals(response.Provider, provider.Id, StringComparison.Ordinal))
            || !string.Equals(response.ConnectionId, connection.SourceConnectionId, StringComparison.Ordinal)
            || response.Account is null
            || !string.Equals(response.Account.Id, connection.AccountId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(response.Account.DisplayName)
            || response.Account.DisplayName.Length > 200
        )
            return false;

        var scopes = response.Scopes is { Count: > 0 }
            ? response.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).ToHashSet(StringComparer.Ordinal)
            : response.GrantedScopes is { Count: > 0 }
                ? response.GrantedScopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).ToHashSet(StringComparer.Ordinal)
            : SplitScopes(response.Scope);
        return scopes.SetEquals(expectedScopes) && scopes.IsSubsetOf(provider.Scopes);
    }

    private void ApplyTokens(HouseholdConsumerConnection connection, ValidatedToken token, DateTime now)
    {
        connection.ProtectedAccessToken = _tokenProtector.Protect(token.AccessToken);
        connection.AccessTokenExpiresAt = now.AddSeconds(token.ExpiresIn);
        connection.ProtectedRefreshToken = _tokenProtector.Protect(token.RefreshToken);
        connection.RefreshTokenExpiresAt = now.AddSeconds(token.RefreshExpiresIn);
        connection.SourceConnectionId = token.ConnectionId;
        connection.AccountId = token.AccountId;
        connection.AccountDisplayName = token.AccountDisplayName;
        connection.GrantedScopes = JoinScopes(token.Scopes);
    }

    private string? TryUnprotectAccessToken(HouseholdConsumerConnection connection)
    {
        try
        {
            return _tokenProtector.Unprotect(connection.ProtectedAccessToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private async Task SetFailureAsync(
        HouseholdConsumerConnection connection,
        HouseholdConnectionStatus status,
        string safeError,
        CancellationToken cancellationToken
    )
    {
        connection.Status = status;
        connection.LastError = safeError;
        connection.LastValidatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private HouseholdCallbackResult CallbackFailure(string provider, string reason)
    {
        var redirectUrl = _providers.BuildCompletionUrl(provider, success: false, reason);
        return new HouseholdCallbackResult(redirectUrl, redirectUrl is not null);
    }

    private static HouseholdConnectionDto ToDto(
        HouseholdProviderDefinition provider,
        HouseholdConsumerConnection connection
    ) =>
        new(
            provider.Id,
            provider.DisplayName,
            provider.Configured,
            provider.OpenUrl,
            connection.Status,
            connection.Status == HouseholdConnectionStatus.Connected ? connection.AccountDisplayName : null,
            connection.Status == HouseholdConnectionStatus.Connected ? connection.AccountId : null,
            SplitScopes(connection.GrantedScopes).Order(StringComparer.Ordinal).ToList(),
            connection.ConnectedAt,
            connection.LastValidatedAt,
            connection.LastError
        );

    private static HouseholdConnectionDto ToDisconnectedDto(HouseholdProviderDefinition provider) =>
        new(
            provider.Id,
            provider.DisplayName,
            provider.Configured,
            provider.OpenUrl,
            HouseholdConnectionStatus.Disconnected,
            null,
            null,
            [],
            null,
            null,
            null
        );

    private static HashSet<string> SplitScopes(string? scopes) =>
        (scopes ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    private static string JoinScopes(IEnumerable<string> scopes) =>
        string.Join(' ', scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private sealed record ProviderResponse<T>(HttpStatusCode? StatusCode, T? Value, bool IsSuccess)
    {
        public static ProviderResponse<T> Success(HttpStatusCode statusCode, T value) => new(statusCode, value, true);

        public static ProviderResponse<T> Failure(HttpStatusCode? statusCode) => new(statusCode, default, false);
    }

    private sealed class TokenResponse
    {
        public string? TokenType { get; set; }
        public string? AccessToken { get; set; }
        public int ExpiresIn { get; set; }
        public string? RefreshToken { get; set; }
        public int RefreshExpiresIn { get; set; }
        public string? Scope { get; set; }
        public string? ConnectionId { get; set; }
        public ProviderAccount? Account { get; set; }
    }

    private sealed class MeResponse
    {
        public string? Provider { get; set; }
        public string? ConnectionId { get; set; }
        public string? Scope { get; set; }
        public List<string>? Scopes { get; set; }
        public List<string>? GrantedScopes { get; set; }
        public ProviderAccount? Account { get; set; }
    }

    private sealed class ProviderAccount
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed record ValidatedToken(
        string AccessToken,
        string RefreshToken,
        int ExpiresIn,
        int RefreshExpiresIn,
        string ConnectionId,
        string AccountId,
        string AccountDisplayName,
        IReadOnlySet<string> Scopes
    );
}
