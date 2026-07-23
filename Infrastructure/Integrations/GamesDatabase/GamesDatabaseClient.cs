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

public class GamesDatabaseClient : IGamesDatabaseClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly GamesDatabaseSettings _settings;
    private readonly IHouseholdProviderAccessService _connectionAccess;

    public GamesDatabaseClient(
        HttpClient httpClient,
        IOptions<GamesDatabaseSettings> settings,
        IHouseholdProviderAccessService connectionAccess
    )
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _connectionAccess = connectionAccess;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 3, 60));
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

        using var request = new HttpRequestMessage(method, $"{access.BaseUrl.TrimEnd('/')}{path}");
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
            if (response.StatusCode == HttpStatusCode.NotFound)
                return default;
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new IntegrationGatewayException(HttpStatusCode.Forbidden, "Games Database denied this operation.");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new IntegrationGatewayException(HttpStatusCode.Conflict, "Reconnect Games Database to continue.");
            if (!response.IsSuccessStatusCode)
                throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Games Database request failed.");

            if (response.Content.Headers.ContentLength == 0)
                return default;

            try
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Games Database returned an invalid response.");
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
        var openUrl = _settings.OpenUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(openUrl) ? null : $"{openUrl}/#/games/{id}";
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
            BuildAssetUrl(game.Logo),
            BuildAssetUrl(game.Cover),
            game.Grade,
            game.Score,
            game.Started,
            game.Finished,
            game.SteamAppId,
            game.SteamPlaytimeForever,
            BuildOpenUrl(game.Id)
        );

    private string? BuildAssetUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            return absolute.Scheme is "http" or "https" && string.IsNullOrEmpty(absolute.UserInfo)
                ? absolute.ToString()
                : null;

        var openUrl = _settings.OpenUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(openUrl) ? null : $"{openUrl}/{path.TrimStart('/')}";
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
        public decimal? Score { get; set; }
        public string? PlatformName { get; set; }
        public string? Started { get; set; }
        public string? Finished { get; set; }
        public string? Logo { get; set; }
        public string? Cover { get; set; }
        public int? SteamAppId { get; set; }
        public int? SteamPlaytimeForever { get; set; }
        public string? StatusName { get; set; }
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
