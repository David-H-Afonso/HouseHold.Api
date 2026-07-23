using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public GamesDatabaseClient(HttpClient httpClient, IOptions<GamesDatabaseSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.TimeoutSeconds, 3, 60));
    }

    public async Task<GamesModuleListDto> GetGamesAsync(
        string? search,
        int? statusId,
        int page,
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        if (!IsConfigured())
            return new GamesModuleListDto([], 0, page, pageSize, 0);

        var query = BuildQuery(
            new Dictionary<string, string?>
            {
                ["search"] = search,
                ["statusId"] = statusId?.ToString(),
                ["page"] = Math.Max(page, 1).ToString(),
                ["pageSize"] = Math.Clamp(pageSize, 1, 100).ToString(),
            }
        );

        var result = await SendAsync<PagedGamesResponse>(HttpMethod.Get, $"/api/games{query}", null, cancellationToken);
        return new GamesModuleListDto(
            (result?.Data ?? []).Select(ToModuleItem).ToList(),
            result?.TotalCount ?? 0,
            result?.Page ?? page,
            result?.PageSize ?? pageSize,
            result?.TotalPages ?? 0
        );
    }

    public async Task<GameModuleItemDto?> GetGameAsync(int id, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
            return null;

        var game = await SendAsync<GameDto>(HttpMethod.Get, $"/api/games/{id}", null, cancellationToken);
        return game is null ? null : ToModuleItem(game);
    }

    public async Task<GameModuleItemDto?> UpdateStatusAsync(
        int id,
        int statusId,
        CancellationToken cancellationToken
    )
    {
        if (!IsConfigured())
            return null;

        await SendAsync<object>(HttpMethod.Put, $"/api/games/{id}", new { statusId }, cancellationToken);
        return await GetGameAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<GameStatusOptionDto>> GetStatusesAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured())
            return [];

        var statuses = await SendAsync<List<GameStatusDto>>(
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

    public async Task<IReadOnlyList<SteamSearchResultDto>> SearchSteamAsync(
        string query,
        CancellationToken cancellationToken
    )
    {
        if (!IsConfigured() || string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return [];

        var encoded = Uri.EscapeDataString(query.Trim());
        var results = await SendAsync<List<SteamStoreSearchItemDto>>(
            HttpMethod.Get,
            $"/api/steam/store/search?q={encoded}",
            null,
            cancellationToken
        );

        return (results ?? []).Select(item => new SteamSearchResultDto(
                item.AppId,
                item.Name,
                item.CoverUrl,
                item.LogoUrl,
                item.Price,
                item.Metascore
            ))
            .ToList();
    }

    public async Task<object?> AddSteamGameAsync(AddSteamGameRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured())
            return null;

        return await SendAsync<object>(
            HttpMethod.Post,
            "/api/steam/store/add",
            new { request.AppId, request.LogoUrl, request.CoverUrl },
            cancellationToken
        );
    }

    public async Task<GamesSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var statuses = await GetStatusesAsync(cancellationToken);
        var games = await GetGamesAsync(null, null, 1, 100, cancellationToken);
        var counts = games.Items
            .GroupBy(game => game.StatusName ?? "Unknown")
            .ToDictionary(group => group.Key, group => group.Count());

        return new GamesSummaryDto(games.TotalCount, statuses, counts);
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken
    )
    {
        var baseUrl = _settings.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return default;

        using var request = new HttpRequestMessage(method, $"{baseUrl}{path}");
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0)
            return default;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private bool IsConfigured() => !string.IsNullOrWhiteSpace(_settings.BaseUrl);

    private string? BuildOpenUrl(int id)
    {
        var openUrl = _settings.OpenUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(openUrl) ? null : $"{openUrl}/games/{id}";
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
            game.Logo,
            game.Cover,
            game.Grade,
            game.Score,
            game.Started,
            game.Finished,
            game.SteamAppId,
            game.SteamPlaytimeForever,
            BuildOpenUrl(game.Id)
        );

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

    private sealed class SteamStoreSearchItemDto
    {
        public int AppId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? CoverUrl { get; set; }
        public string? LogoUrl { get; set; }
        public string? Price { get; set; }
        public int? Metascore { get; set; }
    }

}
