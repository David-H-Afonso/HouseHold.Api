using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.DTOs;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.GamesDatabase;

public class GamesDatabaseClient : HouseholdProviderClientBase, IGamesDatabaseClient
{
    private static readonly new JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly GamesDatabaseSettings _settings;
    private readonly HouseholdConnectionSettings _connectionSettings;
    private readonly IHouseholdProviderAccessService _connectionAccess;

    public GamesDatabaseClient(
        HttpClient httpClient,
        IOptions<GamesDatabaseSettings> settings,
        IOptions<HouseholdConnectionSettings> connectionSettings,
        IHouseholdProviderAccessService connectionAccess
    ) : base(httpClient, connectionAccess, "games-database", "Games Database")
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _connectionSettings = connectionSettings.Value;
        _connectionAccess = connectionAccess;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 3, 60));
    }

    public async Task<(byte[] Content, string ContentType)?> GetAssetAsync(Guid userId, int id, string kind, CancellationToken cancellationToken)
    {
        if (id <= 0 || kind is not ("cover" or "logo" or "hero")) return null;
        var game = await SendAsync<GameDto>(userId, "games.read", HttpMethod.Get, $"/api/games/{id}", null, cancellationToken);
        var source = kind switch
        {
            "cover" => game?.Cover,
            "hero" => game?.Hero,
            _ => game?.Logo,
        };
        var path = BuildProviderPath(source);
        if (path is null) return null;
        var file = await DownloadAsync(userId, "games.read", path, 8 * 1024 * 1024, cancellationToken);
        return file is null ? null : (file.Content, file.ContentType);
    }

    public async Task<GamesModuleListDto> GetGamesAsync(
        Guid userId,
        string? search,
        int? statusId,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var query = BuildQuery(
            new Dictionary<string, string?>
            {
                ["search"] = search,
                ["statusId"] = statusId?.ToString(),
                ["page"] = Math.Max(page, 1).ToString(),
                ["pageSize"] = Math.Clamp(pageSize, 1, 100).ToString(),
            }
        );

        var result = await SendAsync<PagedGamesResponse>(
            userId,
            "games.read",
            HttpMethod.Get,
            $"/api/games{query}",
            null,
            cancellationToken
        );
        return new GamesModuleListDto(
            (result?.Data ?? []).Select(ToModuleItem).ToList(),
            result?.TotalCount ?? 0,
            result?.Page ?? page,
            result?.PageSize ?? pageSize,
            result?.TotalPages ?? 0
        );
    }

    public async Task<GameModuleItemDto?> GetGameAsync(Guid userId, int id, CancellationToken cancellationToken)
    {
        var game = await SendAsync<GameDto>(
            userId,
            "games.read",
            HttpMethod.Get,
            $"/api/games/{id}",
            null,
            cancellationToken
        );
        return game is null ? null : ToModuleItem(game);
    }

    public async Task<GameModuleItemDto?> UpdateStatusAsync(
        Guid userId,
        int id,
        int statusId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var game = await SendAsync<GameDto>(
                userId,
                "games.status.write",
                HttpMethod.Patch,
                $"/api/games/{id}/status",
                new { statusId },
                cancellationToken
            );
            return game is null ? null : ToModuleItem(game);
        }
        catch (IntegrationGatewayException exception) when (
            exception.Code is "ambiguous_timeout" or "ambiguous_transport" or "ambiguous_response" or "invalid_provider_response"
        )
        {
            var canonical = await GetGameAsync(userId, id, cancellationToken);
            if (canonical?.StatusId == statusId) return canonical;
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "Games Database did not confirm the requested status.",
                "mutation_unconfirmed",
                reconcilable: true
            );
        }
    }

    public async Task<IReadOnlyList<GameStatusOptionDto>> GetStatusesAsync(
        Guid userId,
        CancellationToken cancellationToken
    )
    {
        var statuses = await SendAsync<List<GameStatusDto>>(
            userId,
            "games.read",
            HttpMethod.Get,
            "/api/GameStatus/active",
            null,
            cancellationToken
        );

        return (statuses ?? []).Select(status => new GameStatusOptionDto(
                status.Id,
                status.Name,
                status.Color ?? "#ffffff",
                status.StatusType ?? "None"
            ))
            .ToList();
    }

    public async Task<GamesSummaryDto> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var summary = await SendAsync<SourceGamesSummary>(
            userId,
            "games.read",
            HttpMethod.Get,
            "/api/games/summary",
            null,
            cancellationToken
        );
        var statuses = await GetStatusesAsync(userId, cancellationToken);
        var counts = (summary?.ByStatus ?? []).ToDictionary(item => item.StatusName, item => item.Count);

        return new GamesSummaryDto(summary?.TotalGames ?? 0, statuses, counts);
    }

    private async Task<T?> SendAsync<T>(
        Guid userId,
        string requiredScope,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool retrying = false,
        string? failedTokenVersion = null
    )
    {
        var access = await _connectionAccess.GetAccessAsync(
            userId,
            "games-database",
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
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && method != HttpMethod.Get)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "Games Database may have applied the change; canonical state must be checked.",
                "ambiguous_timeout",
                reconcilable: true
            );
        }
        catch (HttpRequestException) when (method != HttpMethod.Get)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "Games Database may have applied the change; canonical state must be checked.",
                "ambiguous_transport",
                reconcilable: true
            );
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        )
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Games Database is unavailable.");
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && !retrying)
            {
                return await SendAsync<T>(
                    userId,
                    requiredScope,
                    method,
                    path,
                    body,
                    cancellationToken,
                    retrying: true,
                    failedTokenVersion: access.TokenVersion
                );
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new IntegrationGatewayException(HttpStatusCode.Forbidden, "Games Database denied this operation.", "permission_missing");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new IntegrationGatewayException(HttpStatusCode.Conflict, "Reconnect Games Database to continue.");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (method == HttpMethod.Get) return default;
                throw new IntegrationGatewayException(HttpStatusCode.NotFound, "Games Database game was not found.", "provider_not_found");
            }
            if (!response.IsSuccessStatusCode)
            {
                var ambiguous = method != HttpMethod.Get && (int)response.StatusCode >= 500;
                throw new IntegrationGatewayException(
                    method == HttpMethod.Get
                        ? HttpStatusCode.BadGateway
                        : ambiguous ? HttpStatusCode.Conflict : response.StatusCode,
                    "Games Database request failed.",
                    method == HttpMethod.Get
                        ? "provider_request_failed"
                        : ambiguous ? "ambiguous_response" : "provider_request_rejected",
                    reconcilable: ambiguous
                );
            }

            if (response.Content.Headers.ContentLength == 0)
            {
                if (method == HttpMethod.Get) return default;
                throw new IntegrationGatewayException(
                    HttpStatusCode.Conflict,
                    "Games Database returned an empty mutation response.",
                    "ambiguous_response",
                    reconcilable: true
                );
            }

            try
            {
                await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken);
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or HttpRequestException or NotSupportedException)
            {
                throw new IntegrationGatewayException(
                    method == HttpMethod.Get ? HttpStatusCode.BadGateway : HttpStatusCode.Conflict,
                    "Games Database returned an invalid response.",
                    "invalid_provider_response",
                    reconcilable: method != HttpMethod.Get
                );
            }
        }
    }

    private static IntegrationGatewayException ToGatewayException(HouseholdProviderAccessStatus status) =>
        status switch
        {
            HouseholdProviderAccessStatus.MissingScope =>
                new IntegrationGatewayException(HttpStatusCode.Forbidden, "Games Database permission is missing."),
            HouseholdProviderAccessStatus.ProviderUnavailable =>
                new IntegrationGatewayException(HttpStatusCode.BadGateway, "Games Database is unavailable."),
            _ => new IntegrationGatewayException(HttpStatusCode.Conflict, "Connect Games Database to continue."),
        };

    private string? BuildOpenUrl(int id)
    {
        var openUrl = NormalizePublicBaseUrl();
        return openUrl is null ? null : $"{openUrl}/#/games/{id}";
    }

    private static string BuildQuery(Dictionary<string, string?> values)
    {
        var pairs = values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}")
            .ToList();
        return pairs.Count == 0 ? string.Empty : $"?{string.Join("&", pairs)}";
    }

    private GameModuleItemDto ToModuleItem(GameDto game) =>
        new(
            game.Id,
            game.Name,
            game.StatusId,
            game.StatusName,
            game.PlatformName,
            BuildAssetUrl(game.Id, "logo", game.Logo),
            BuildAssetUrl(game.Id, "cover", game.Cover),
            BuildAssetUrl(game.Id, "hero", game.Hero),
            game.Grade,
            game.Score,
            game.Started,
            game.Finished,
            game.SteamAppId,
            game.SteamPlaytimeForever,
            game.Favorite,
            game.Released,
            game.Comment,
            game.Critic,
            game.CriticProvider,
            game.Story,
            game.Completion,
            game.PlayedStatusName,
            game.PlayWithNames,
            game.CreatedAt,
            game.UpdatedAt,
            BuildOpenUrl(game.Id)
        );

    private static string? BuildProviderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (
            Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            && absolute.Scheme is ("http" or "https")
        )
            return string.IsNullOrEmpty(absolute.UserInfo) ? absolute.PathAndQuery : null;
        return path[0] == '/' && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\') && !path.Any(char.IsControl) ? path : null;
    }

    private string? BuildAssetUrl(int id, string kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (
            Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            && absolute.Scheme is ("http" or "https")
        )
        {
            var configured = NormalizePublicBaseUrl();
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
                || !string.Equals(absolute.Scheme, configuredUri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(absolute.Host, configuredUri.Host, StringComparison.OrdinalIgnoreCase)
                || absolute.Port != configuredUri.Port
                || !string.IsNullOrEmpty(absolute.UserInfo))
                return null;
        }
        else if (BuildProviderPath(path) is null)
        {
            return null;
        }

        return $"/modules/games/assets/{id}/{kind}";
    }

    private string? NormalizePublicBaseUrl()
    {
        var candidate = _connectionSettings.GamesDatabaseOpenUrl?.Trim().TrimEnd('/');
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo)
                ? candidate
                : null;
    }

    private static Uri BuildRequestUri(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path[0] != '/'
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\')
            || path.Any(char.IsControl)
            || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || !Uri.TryCreate(
                $"{origin.Scheme}://{origin.Authority}/{path.TrimStart('/')}",
                UriKind.Absolute,
                out var combined
            )
            || !string.Equals(origin.Scheme, combined.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Host, combined.Host, StringComparison.OrdinalIgnoreCase)
            || origin.Port != combined.Port)
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Games Database path is invalid.", "invalid_provider_path");
        return combined;
    }

    private sealed class PagedGamesResponse
    {
        public List<GameDto> Data { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class GameDto
    {
        public int Id { get; set; }
        public int StatusId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Grade { get; set; }
        public int? Critic { get; set; }
        public string? CriticProvider { get; set; }
        public int? Story { get; set; }
        public int? Completion { get; set; }
        public decimal? Score { get; set; }
        public string? PlatformName { get; set; }
        public string? Started { get; set; }
        public string? Finished { get; set; }
        public string? Released { get; set; }
        public string? Comment { get; set; }
        public string? Logo { get; set; }
        public string? Cover { get; set; }
        public string? Hero { get; set; }
        public int? SteamAppId { get; set; }
        public int? SteamPlaytimeForever { get; set; }
        public string? StatusName { get; set; }
        public bool Favorite { get; set; }
        public string? PlayedStatusName { get; set; }
        public List<string> PlayWithNames { get; set; } = [];
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private sealed class GameStatusDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public string? StatusType { get; set; }
    }

    private sealed class SourceGamesSummary
    {
        public int TotalGames { get; set; }
        public List<SourceGameSummaryStatus> ByStatus { get; set; } = [];
    }

    private sealed class SourceGameSummaryStatus
    {
        public string StatusName { get; set; } = string.Empty;
        public int Count { get; set; }
    }

}
