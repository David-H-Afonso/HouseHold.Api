using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Auth;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.Seerr;

public sealed class SeerrService : ISeerrService
{
    private const string IntegrationName = "Seerr";
    private const string ApiKeySecret = "api-key";

    // Seerr permission bitmask
    private const int PermAdmin = 2;
    private const int PermManageRequests = 16;
    private const int PermRequest = 32;
    private const int PermRequest4k = 1024;
    private const int PermRequest4kMovie = 2048;
    private const int PermRequest4kTv = 4096;
    private const int PermViewRequests = 16384;
    private const int PermRequestMovie = 262144;
    private const int PermRequestTv = 524288;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 24 };
    private static readonly SemaphoreSlim ConfigWriteLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IDataProtector _protector;
    private readonly SeerrSettings _settings;
    private readonly ILogger<SeerrService> _logger;
    private static readonly ConcurrentDictionary<Guid, UserMapCacheEntry> UserMapCache = new();

    public SeerrService(
        AppDbContext db,
        HttpClient httpClient,
        IDataProtectionProvider protectionProvider,
        IOptions<SeerrSettings> settings,
        ILogger<SeerrService> logger
    )
    {
        _db = db;
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.RequestTimeoutSeconds, 3, 60));
        _protector = protectionProvider.CreateProtector("Household.Seerr.ApiKey.v1");
        _logger = logger;
    }

    // ── Admin config ────────────────────────────────────────────────────────────
    public async Task EnsureBootstrapConfigAsync(CancellationToken cancellationToken)
    {
        if (await _db.Integrations.AnyAsync(
                item => item.Type == IntegrationType.Seerr && item.Name == IntegrationName,
                cancellationToken))
            return;

        var hasUrl = !string.IsNullOrWhiteSpace(_settings.BaseUrl);
        var hasApiKey = !string.IsNullOrWhiteSpace(_settings.ApiKey);
        if (!hasUrl && !hasApiKey) return;
        if (!hasUrl || !hasApiKey)
        {
            _logger.LogWarning("Seerr environment bootstrap requires both SEERR_BASE_URL and SEERR_API_KEY.");
            return;
        }

        var internalUrl = NormalizeHttpUrl(_settings.BaseUrl!);
        var publicUrl = string.IsNullOrWhiteSpace(_settings.PublicUrl)
            ? internalUrl
            : NormalizeHttpUrl(_settings.PublicUrl);
        var integration = new Integration
        {
            Type = IntegrationType.Seerr,
            Name = IntegrationName,
            BaseUrl = internalUrl,
            OpenUrl = publicUrl,
            Enabled = true,
        };
        integration.Secrets.Add(new IntegrationSecret
        {
            SecretKey = ApiKeySecret,
            ProtectedValue = _protector.Protect(_settings.ApiKey!.Trim()),
        });
        _db.Integrations.Add(integration);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded Seerr configuration from environment variables.");
    }

    public async Task<SeerrConfigDto> GetConfigAsync(CancellationToken cancellationToken)
    {
        var integration = await LoadIntegrationAsync(cancellationToken);
        var connection = ToConnection(integration);
        string? version = null;
        var reachable = false;
        if (connection is not null)
        {
            try
            {
                using var response = await SendRawAsync(connection, HttpMethod.Get, "/api/v1/status", null, null, cancellationToken);
                reachable = response.IsSuccessStatusCode;
                if (reachable)
                {
                    using var doc = await ReadJsonAsync(response, cancellationToken);
                    version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
                    using var authenticatedResponse = await SendRawAsync(
                        connection,
                        HttpMethod.Get,
                        "/api/v1/user?take=1",
                        null,
                        null,
                        cancellationToken);
                    reachable = authenticatedResponse.IsSuccessStatusCode;
                }
            }
            catch (IntegrationGatewayException) { reachable = false; }
        }
        return new SeerrConfigDto(
            connection is not null,
            integration?.BaseUrl,
            integration?.OpenUrl,
            integration?.Secrets.Any(s => s.SecretKey == ApiKeySecret) == true,
            version,
            reachable
        );
    }

    public async Task<SeerrConfigDto> UpdateConfigAsync(UpdateSeerrConfigRequest request, CancellationToken cancellationToken)
    {
        var internalUrl = NormalizeHttpUrl(request.InternalUrl);
        var publicUrl = NormalizeHttpUrl(request.PublicUrl);
        var apiKey = request.ApiKey?.Trim();

        await ConfigWriteLock.WaitAsync(cancellationToken);
        try
        {
            _db.ChangeTracker.Clear();
            var integration = await LoadIntegrationAsync(cancellationToken) ?? new Integration
            {
                Type = IntegrationType.Seerr,
                Name = IntegrationName,
            };
            var existingSecret = integration.Secrets.SingleOrDefault(s => s.SecretKey == ApiKeySecret);
            if (existingSecret is null && string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("A Seerr API key is required for initial configuration.");
            if (existingSecret is not null
                && !string.IsNullOrWhiteSpace(integration.BaseUrl)
                && !string.Equals(integration.BaseUrl, internalUrl, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Provide the Seerr API key again when changing the internal URL.");
            if (_db.Entry(integration).State == EntityState.Detached) _db.Integrations.Add(integration);
            integration.BaseUrl = internalUrl;
            integration.OpenUrl = publicUrl;
            integration.Enabled = true;
            integration.ConfigurationVersion = Guid.NewGuid();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                if (apiKey.Length > 1000) throw new ArgumentException("Seerr API key is invalid.");
                var secret = existingSecret;
                if (secret is null)
                {
                    secret = new IntegrationSecret { SecretKey = ApiKeySecret };
                    integration.Secrets.Add(secret);
                }
                secret.ProtectedValue = _protector.Protect(apiKey);
            }
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new IntegrationGatewayException(
                    HttpStatusCode.Conflict,
                    "Seerr configuration changed while this update was in progress. Reload and try again.",
                    "seerr_config_conflict");
            }
        }
        finally
        {
            ConfigWriteLock.Release();
        }

        UserMapCache.Clear();
        return await GetConfigAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SeerrUserMappingDto>> GetUserMappingsAsync(CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking()
            .OrderBy(user => user.UserName)
            .Select(user => new SeerrUserMappingDto(
                user.Id,
                user.UserName,
                user.Preference == null ? null : user.Preference.JellyfinUserId,
                user.Preference != null && user.Preference.SeerrJellyfinMappingApproved,
                user.Preference == null ? null : user.Preference.SeerrUserIdOverride,
                user.Preference != null && user.Preference.SeerrUserIdOverride != null
                    ? "override"
                    : user.Preference != null && user.Preference.SeerrJellyfinMappingApproved
                        ? "jellyfin"
                        : null))
            .ToListAsync(cancellationToken);

    public async Task<SeerrUserMappingDto> UpdateUserMappingAsync(
        Guid actorUserId,
        Guid targetUserId,
        UpdateSeerrUserMappingRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _db.Users.Include(item => item.Preference)
            .SingleOrDefaultAsync(item => item.Id == targetUserId, cancellationToken)
            ?? throw new IntegrationGatewayException(HttpStatusCode.NotFound, "Household user was not found.", "user_not_found");
        var connection = await RequireConnectionAsync(cancellationToken);
        var source = request.Source?.Trim().ToLowerInvariant();
        var preference = user.Preference ?? new UserPreference { UserId = user.Id };
        if (user.Preference is null) _db.UserPreferences.Add(preference);
        var resolvedSeerrUserId = 0;

        switch (source)
        {
            case "jellyfin":
            {
                var jellyfinUserId = NormalizeJellyfinUserId(request.JellyfinUserId);
                if (await _db.UserPreferences.AsNoTracking().AnyAsync(
                        item => item.UserId != targetUserId
                            && item.SeerrJellyfinMappingApproved
                            && item.JellyfinUserId == jellyfinUserId,
                        cancellationToken))
                    throw new IntegrationGatewayException(
                        HttpStatusCode.Conflict,
                        "This Jellyfin identity is already assigned to another Household user.",
                        "seerr_mapping_conflict");
                using var response = await SendRawAsync(
                    connection,
                    HttpMethod.Get,
                    $"/api/v1/user/jellyfin/{Uri.EscapeDataString(jellyfinUserId)}",
                    null,
                    null,
                    cancellationToken);
                if (!response.IsSuccessStatusCode) throw MapError(response.StatusCode);
                using var document = await ReadJsonAsync(response, cancellationToken);
                if (!document.RootElement.TryGetProperty("id", out var idElement)
                    || !idElement.TryGetInt32(out var mappedId)
                    || mappedId <= 0)
                    throw new IntegrationGatewayException(
                        HttpStatusCode.BadGateway,
                        "Seerr returned an invalid user mapping.",
                        "invalid_provider_response");
                preference.JellyfinUserId = jellyfinUserId;
                preference.SeerrJellyfinMappingApproved = true;
                preference.SeerrUserIdOverride = null;
                resolvedSeerrUserId = mappedId;
                break;
            }
            case "override":
            {
                if (request.SeerrUserId is not > 0)
                    throw new ArgumentException("A valid Seerr user ID is required.");
                if (await _db.UserPreferences.AsNoTracking().AnyAsync(
                        item => item.UserId != targetUserId
                            && item.SeerrUserIdOverride == request.SeerrUserId.Value,
                        cancellationToken))
                    throw new IntegrationGatewayException(
                        HttpStatusCode.Conflict,
                        "This Seerr identity is already assigned to another Household user.",
                        "seerr_mapping_conflict");
                using var document = await GetJsonAsync(
                    connection,
                    null,
                    $"/api/v1/user/{request.SeerrUserId.Value}",
                    cancellationToken);
                if (!document.RootElement.TryGetProperty("id", out var idElement)
                    || !idElement.TryGetInt32(out var resolvedId)
                    || resolvedId != request.SeerrUserId.Value)
                    throw new IntegrationGatewayException(
                        HttpStatusCode.BadGateway,
                        "Seerr returned an invalid user.",
                        "invalid_provider_response");
                preference.SeerrUserIdOverride = resolvedId;
                preference.SeerrJellyfinMappingApproved = false;
                resolvedSeerrUserId = resolvedId;
                break;
            }
            default:
                throw new ArgumentException("Mapping source must be jellyfin or override.");
        }

        if (await _db.UserPreferences.AsNoTracking().AnyAsync(
                item => item.UserId != targetUserId
                    && item.SeerrResolvedUserId == resolvedSeerrUserId,
                cancellationToken))
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "This Seerr identity is already assigned to another Household user.",
                "seerr_mapping_conflict");
        preference.SeerrResolvedUserId = resolvedSeerrUserId;

        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Action = "seerr.user_mapping_updated",
            SummaryJson = JsonSerializer.Serialize(new { source }, JsonOptions),
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "This Seerr identity is already assigned to another Household user.",
                "seerr_mapping_conflict");
        }
        UserMapCache.TryRemove(targetUserId, out _);
        return ToMappingDto(user, preference);
    }

    public async Task ClearUserMappingAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var preference = await _db.UserPreferences.SingleOrDefaultAsync(
            item => item.UserId == targetUserId,
            cancellationToken);
        if (!await _db.Users.AnyAsync(item => item.Id == targetUserId, cancellationToken))
            throw new IntegrationGatewayException(HttpStatusCode.NotFound, "Household user was not found.", "user_not_found");
        if (preference is not null)
        {
            preference.SeerrJellyfinMappingApproved = false;
            preference.SeerrUserIdOverride = null;
            preference.SeerrResolvedUserId = null;
        }
        _db.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Action = "seerr.user_mapping_cleared",
        });
        await _db.SaveChangesAsync(cancellationToken);
        UserMapCache.TryRemove(targetUserId, out _);
    }

    // ── Session ───────────────────────────────────────────────────────────────────
    public async Task<SeerrSessionDto> GetSessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var connection = ToConnection(await LoadIntegrationAsync(cancellationToken));
        if (connection is null)
            return new SeerrSessionDto(false, false, null, null, null, null, 0, false, false, false, false, false, false, null, null);
        var mapping = await ResolveSeerrUserAsync(connection, userId, cancellationToken);
        if (mapping is null)
            return new SeerrSessionDto(
                true, false, null, null, null, connection.PublicUrl, 0,
                false, false, false, false, false, false, null, null);

        var seerrUserId = mapping.SeerrUserId;
        using var userDoc = await GetJsonAsync(connection, seerrUserId, $"/api/v1/user/{seerrUserId}", cancellationToken);
        var root = userDoc.RootElement;
        var permissions = root.TryGetProperty("permissions", out var p) && p.TryGetInt32(out var perm) ? perm : 0;
        var displayName = root.TryGetProperty("displayName", out var d) ? d.GetString()
            : root.TryGetProperty("username", out var u) ? u.GetString() : null;

        var admin = (permissions & PermAdmin) != 0;
        var canMovies = admin || (permissions & (PermRequest | PermRequestMovie)) != 0;
        var canTv = admin || (permissions & (PermRequest | PermRequestTv)) != 0;
        var can4kMovies = admin || (permissions & (PermRequest4k | PermRequest4kMovie)) != 0;
        var can4kTv = admin || (permissions & (PermRequest4k | PermRequest4kTv)) != 0;
        var canManage = admin || (permissions & PermManageRequests) != 0;
        var canViewAll = canManage || (permissions & PermViewRequests) != 0;

        SeerrQuotaDto? movieQuota = null, tvQuota = null;
        try
        {
            using var quotaDoc = await GetJsonAsync(connection, seerrUserId, $"/api/v1/user/{seerrUserId}/quota", cancellationToken);
            movieQuota = ReadQuota(quotaDoc.RootElement, "movie");
            tvQuota = ReadQuota(quotaDoc.RootElement, "tv");
        }
        catch (IntegrationGatewayException) { /* quota is optional */ }

        return new SeerrSessionDto(
            true,
            true,
            seerrUserId,
            displayName,
            mapping.Source,
            connection.PublicUrl,
            permissions,
            canMovies,
            canTv,
            can4kMovies,
            can4kTv,
            canManage,
            canViewAll,
            movieQuota,
            tvQuota);
    }

    // ── Discovery / search ─────────────────────────────────────────────────────────
    public async Task<SeerrSearchResponseDto> SearchAsync(Guid userId, string query, int page, CancellationToken cancellationToken)
    {
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > 256)
            throw new ArgumentException("Search query is invalid.");
        var connection = await RequireConnectionAsync(cancellationToken);
        var seerrUserId = await RequireMappedUserIdAsync(connection, userId, cancellationToken);
        var path = $"/api/v1/search?query={Uri.EscapeDataString(trimmed)}&page={ClampPage(page)}";
        using var doc = await GetJsonAsync(connection, seerrUserId, path, cancellationToken);
        return MapSearch(doc.RootElement, connection.PublicUrl, seerrUserId);
    }

    public async Task<SeerrSearchResponseDto> DiscoverAsync(Guid userId, string kind, int page, CancellationToken cancellationToken)
    {
        var path = kind switch
        {
            "trending" => $"/api/v1/discover/trending?page={ClampPage(page)}",
            "movies" => $"/api/v1/discover/movies?page={ClampPage(page)}",
            "tv" => $"/api/v1/discover/tv?page={ClampPage(page)}",
            "upcoming-movies" => $"/api/v1/discover/movies/upcoming?page={ClampPage(page)}",
            "upcoming-tv" => $"/api/v1/discover/tv/upcoming?page={ClampPage(page)}",
            _ => throw new ArgumentException("Unsupported discover category."),
        };
        var connection = await RequireConnectionAsync(cancellationToken);
        var seerrUserId = await RequireMappedUserIdAsync(connection, userId, cancellationToken);
        using var doc = await GetJsonAsync(connection, seerrUserId, path, cancellationToken);
        return MapSearch(doc.RootElement, connection.PublicUrl, seerrUserId);
    }

    public Task<SeerrDetailDto> GetMovieAsync(Guid userId, int tmdbId, CancellationToken cancellationToken) =>
        GetDetailAsync(userId, "movie", tmdbId, cancellationToken);

    public Task<SeerrDetailDto> GetTvAsync(Guid userId, int tmdbId, CancellationToken cancellationToken) =>
        GetDetailAsync(userId, "tv", tmdbId, cancellationToken);

    private async Task<SeerrDetailDto> GetDetailAsync(Guid userId, string mediaType, int tmdbId, CancellationToken cancellationToken)
    {
        if (tmdbId <= 0) throw new ArgumentException("Invalid media id.");
        var connection = await RequireConnectionAsync(cancellationToken);
        var seerrUserId = await RequireMappedUserIdAsync(connection, userId, cancellationToken);
        var path = $"/api/v1/{mediaType}/{tmdbId}";
        using var doc = await GetJsonAsync(connection, seerrUserId, path, cancellationToken);
        var root = doc.RootElement;

        var title = GetString(root, "title") ?? GetString(root, "name") ?? "Untitled";
        var date = GetString(root, "releaseDate") ?? GetString(root, "firstAirDate");
        var genres = root.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array
            ? g.EnumerateArray().Select(x => GetString(x, "name")).Where(x => x is not null).Select(x => x!).Take(10).ToList()
            : new List<string>();
        int? runtime = root.TryGetProperty("runtime", out var rt) && rt.TryGetInt32(out var rtv)
            ? rtv
            : null;
        if (runtime is null
            && root.TryGetProperty("episodeRunTime", out var episodeRuntime)
            && episodeRuntime.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in episodeRuntime.EnumerateArray())
            {
                if (!value.TryGetInt32(out var episodeMinutes)) continue;
                runtime = episodeMinutes;
                break;
            }
        }

        var seasonStatuses = ReadSeasonStatuses(root);
        var seasons = new List<SeerrSeasonDto>();
        if (mediaType == "tv" && root.TryGetProperty("seasons", out var seasonsEl) && seasonsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in seasonsEl.EnumerateArray())
            {
                var num = s.TryGetProperty("seasonNumber", out var sn) && sn.TryGetInt32(out var snv) ? snv : -1;
                if (num < 0) continue;
                var epCount = s.TryGetProperty("episodeCount", out var ec) && ec.TryGetInt32(out var ecv) ? ecv : 0;
                seasonStatuses.TryGetValue(num, out var statuses);
                seasons.Add(new SeerrSeasonDto(
                    num,
                    GetString(s, "name"),
                    epCount,
                    statuses.Status,
                    statuses.Status4k));
            }
        }

        var (mediaStatus, mediaStatus4k, requestStatus) = ReadMediaInfo(root, seerrUserId);
        return new SeerrDetailDto(
            mediaType,
            tmdbId,
            title,
            YearFromDate(date),
            ToImageUrl(connection.PublicUrl, GetString(root, "posterPath"), "w600_and_h900_bestv2"),
            ToImageUrl(connection.PublicUrl, GetString(root, "backdropPath"), "w1920_and_h800_multi_faces"),
            GetString(root, "overview"),
            GetDouble(root, "voteAverage"),
            runtime,
            genres,
            seasons,
            mediaStatus,
            mediaStatus4k,
            requestStatus,
            GetString(root, "imdbId")
                ?? (root.TryGetProperty("externalIds", out var externalIds)
                    ? GetString(externalIds, "imdbId")
                    : null),
            root.TryGetProperty("externalIds", out var ext) && ext.TryGetProperty("tvdbId", out var tvdb) && tvdb.TryGetInt32(out var tvdbv) ? tvdbv : null
        );
    }

    // ── Requests ───────────────────────────────────────────────────────────────────
    public async Task<SeerrRequestListDto> GetRequestsAsync(Guid userId, string filter, bool mineOnly, int page, CancellationToken cancellationToken)
    {
        var connection = await RequireConnectionAsync(cancellationToken);
        var seerrUserId = await RequireMappedUserIdAsync(connection, userId, cancellationToken);
        if (!mineOnly && !await CanViewAllRequestsAsync(connection, seerrUserId, cancellationToken))
            throw new IntegrationGatewayException(
                HttpStatusCode.Forbidden,
                "You do not have permission to view all Seerr requests.",
                "seerr_forbidden");
        var normalizedFilter = NormalizeRequestFilter(filter);
        var take = 20;
        var skip = (ClampPage(page) - 1) * take;
        var path = $"/api/v1/request?take={take}&skip={skip}&filter={normalizedFilter}&sort=modified";
        if (mineOnly)
            path += $"&requestedBy={seerrUserId}";
        using var doc = await GetJsonAsync(connection, seerrUserId, path, cancellationToken);
        var root = doc.RootElement;
        var pageInfo = root.TryGetProperty("pageInfo", out var pi) ? pi : default;
        var results = new List<SeerrRequestDto>();
        if (root.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
            results.AddRange(arr.EnumerateArray().Select(item => MapRequest(item, seerrUserId, connection.PublicUrl)));
        var pages = pageInfo.ValueKind == JsonValueKind.Object && pageInfo.TryGetProperty("pages", out var pgs) && pgs.TryGetInt32(out var pgv) ? pgv : 1;
        var total = pageInfo.ValueKind == JsonValueKind.Object && pageInfo.TryGetProperty("results", out var tr) && tr.TryGetInt32(out var trv) ? trv : results.Count;
        return new SeerrRequestListDto(ClampPage(page), pages, total, results);
    }

    public async Task<SeerrRequestDto> CreateRequestAsync(Guid userId, CreateSeerrRequestBody body, CancellationToken cancellationToken)
    {
        if (body.MediaType is not ("movie" or "tv")) throw new ArgumentException("Invalid media type.");
        if (body.MediaId <= 0) throw new ArgumentException("Invalid media id.");
        var connection = await RequireConnectionAsync(cancellationToken);
        var seerrUserId = await RequireMappedUserIdAsync(connection, userId, cancellationToken);

        var payload = new Dictionary<string, object?>
        {
            ["mediaType"] = body.MediaType,
            ["mediaId"] = body.MediaId,
            ["is4k"] = body.Is4k,
        };
        if (body.MediaType == "tv")
        {
            if (body.Seasons is { Count: > 100 }
                || body.Seasons?.Any(season => season is < 0 or > 200) == true)
                throw new ArgumentException("Season selection is invalid.");
            var seasons = (body.Seasons ?? []).Distinct().ToList();
            if (seasons.Count == 0) throw new ArgumentException("Select at least one season.");
            payload["seasons"] = seasons;
        }
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendRawAsync(connection, HttpMethod.Post, "/api/v1/request", seerrUserId, content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted)
            throw new IntegrationGatewayException(HttpStatusCode.Conflict, "No seasons were available to request.", "seerr_no_seasons");
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new IntegrationGatewayException(HttpStatusCode.Conflict, "A request for this title already exists.", "seerr_duplicate_request");
        if (!response.IsSuccessStatusCode)
            throw MapError(response.StatusCode);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        return MapRequest(doc.RootElement, seerrUserId, connection.PublicUrl);
    }

    public async Task ModerateRequestAsync(Guid userId, int requestId, string action, CancellationToken cancellationToken)
    {
        if (requestId <= 0) throw new ArgumentException("Invalid request id.");
        if (action is not ("approve" or "decline" or "retry")) throw new ArgumentException("Unsupported action.");
        var connection = await RequireConnectionAsync(cancellationToken);
        var seerrUserId = await RequireMappedUserIdAsync(connection, userId, cancellationToken);
        if (!await CanManageRequestsAsync(connection, seerrUserId, cancellationToken))
            throw new IntegrationGatewayException(
                HttpStatusCode.Forbidden,
                "You do not have permission to manage Seerr requests.",
                "seerr_forbidden");
        using var response = await SendRawAsync(connection, HttpMethod.Post, $"/api/v1/request/{requestId}/{action}", seerrUserId, null, cancellationToken);
        if (!response.IsSuccessStatusCode) throw MapError(response.StatusCode);
    }

    public async Task DeleteRequestAsync(Guid userId, int requestId, CancellationToken cancellationToken)
    {
        if (requestId <= 0) throw new ArgumentException("Invalid request id.");
        var connection = await RequireConnectionAsync(cancellationToken);
        var seerrUserId = await RequireMappedUserIdAsync(connection, userId, cancellationToken);
        using var response = await SendRawAsync(connection, HttpMethod.Delete, $"/api/v1/request/{requestId}", seerrUserId, null, cancellationToken);
        if (!response.IsSuccessStatusCode) throw MapError(response.StatusCode);
    }

    // ── Mapping ────────────────────────────────────────────────────────────────────
    private async Task<int> RequireMappedUserIdAsync(
        Connection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var mapping = await ResolveSeerrUserAsync(connection, userId, cancellationToken);
        return mapping?.SeerrUserId
            ?? throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "Your Household account is not linked to a Seerr user.",
                "seerr_user_not_mapped");
    }

    private async Task<UserMapping?> ResolveSeerrUserAsync(
        Connection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preference = await _db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new
            {
                p.JellyfinUserId,
                p.SeerrJellyfinMappingApproved,
                p.SeerrUserIdOverride,
                p.SeerrResolvedUserId,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (preference?.SeerrUserIdOverride is > 0)
        {
            if (preference.SeerrResolvedUserId != preference.SeerrUserIdOverride.Value)
                await PersistResolvedIdentityAsync(userId, preference.SeerrUserIdOverride.Value, cancellationToken);
            return new UserMapping(preference.SeerrUserIdOverride.Value, "override");
        }
        if (preference is null
            || !preference.SeerrJellyfinMappingApproved
            || string.IsNullOrWhiteSpace(preference.JellyfinUserId))
            return null;

        var jellyfinUserId = preference.JellyfinUserId;
        var sourceKey = $"{connection.IntegrationId:N}:{connection.ConfigurationVersion:N}:{jellyfinUserId}";
        if (UserMapCache.TryGetValue(userId, out var cached)
            && cached.ExpiresAt > DateTime.UtcNow
            && cached.SourceKey == sourceKey)
        {
            if (preference.SeerrResolvedUserId != cached.SeerrUserId)
                await PersistResolvedIdentityAsync(userId, cached.SeerrUserId, cancellationToken);
            return new UserMapping(cached.SeerrUserId, "jellyfin");
        }

        using var response = await SendRawAsync(
            connection,
            HttpMethod.Get,
            $"/api/v1/user/jellyfin/{Uri.EscapeDataString(jellyfinUserId)}",
            null,
            null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw MapError(response.StatusCode);
        using var doc = await ReadJsonAsync(response, cancellationToken);
        if (!doc.RootElement.TryGetProperty("id", out var idEl)
            || !idEl.TryGetInt32(out var id)
            || id <= 0)
            throw new IntegrationGatewayException(
                HttpStatusCode.BadGateway,
                "Seerr returned an invalid user mapping.",
                "invalid_provider_response");
        UserMapCache[userId] = new UserMapCacheEntry(
            sourceKey,
            id,
            DateTime.UtcNow.AddSeconds(Math.Clamp(_settings.UserMappingCacheSeconds, 30, 86400)));
        if (preference.SeerrResolvedUserId != id)
            await PersistResolvedIdentityAsync(userId, id, cancellationToken);
        return new UserMapping(id, "jellyfin");
    }

    private async Task PersistResolvedIdentityAsync(
        Guid userId,
        int seerrUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _db.UserPreferences
                .Where(preference => preference.UserId == userId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        preference => preference.SeerrResolvedUserId,
                        seerrUserId),
                    cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new IntegrationGatewayException(
                HttpStatusCode.Conflict,
                "This Seerr identity is already assigned to another Household user.",
                "seerr_mapping_conflict");
        }
    }

    private async Task<bool> CanManageRequestsAsync(
        Connection connection,
        int seerrUserId,
        CancellationToken cancellationToken)
    {
        using var userDocument = await GetJsonAsync(
            connection,
            seerrUserId,
            $"/api/v1/user/{seerrUserId}",
            cancellationToken);
        var permissions = userDocument.RootElement.TryGetProperty("permissions", out var value)
            && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
        return (permissions & (PermAdmin | PermManageRequests)) != 0;
    }

    private async Task<bool> CanViewAllRequestsAsync(
        Connection connection,
        int seerrUserId,
        CancellationToken cancellationToken)
    {
        using var userDocument = await GetJsonAsync(
            connection,
            seerrUserId,
            $"/api/v1/user/{seerrUserId}",
            cancellationToken);
        var permissions = userDocument.RootElement.TryGetProperty("permissions", out var value)
            && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
        return (permissions & (PermAdmin | PermManageRequests | PermViewRequests)) != 0;
    }

    // ── HTTP plumbing ──────────────────────────────────────────────────────────────
    private async Task<JsonDocument> GetJsonAsync(Connection connection, int? seerrUserId, string path, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(connection, HttpMethod.Get, path, seerrUserId, null, cancellationToken);
        if (!response.IsSuccessStatusCode) throw MapError(response.StatusCode);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        Connection connection,
        HttpMethod method,
        string path,
        int? seerrUserId,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{connection.BaseUrl}{path}");
        request.Headers.Add("X-Api-Key", connection.ApiKey);
        if (seerrUserId is not null)
            request.Headers.Add("X-API-User", seerrUserId.Value.ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Household", "1.0"));
        if (content is not null) request.Content = content;
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Seerr is unavailable.", "seerr_unavailable");
        }
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bodyTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_settings.RequestTimeoutSeconds, 3, 60)));
        try
        {
            await response.Content.LoadIntoBufferAsync(
                Math.Clamp(_settings.MaxJsonBytes, 1024, 8 * 1024 * 1024),
                bodyTimeout.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(bodyTimeout.Token);
            return await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 24 }, bodyTimeout.Token);
        }
        catch (JsonException)
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Seerr returned an invalid response.", "invalid_provider_response");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Seerr response timed out.", "seerr_unavailable");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Seerr returned an invalid response.", "invalid_provider_response");
        }
    }

    private static IntegrationGatewayException MapError(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new(HttpStatusCode.Forbidden, "You do not have permission for this Seerr action.", "seerr_forbidden"),
        HttpStatusCode.Forbidden => new(HttpStatusCode.Forbidden, "You do not have permission for this Seerr action.", "seerr_forbidden"),
        HttpStatusCode.NotFound => new(HttpStatusCode.NotFound, "The Seerr resource was not found.", "seerr_not_found"),
        HttpStatusCode.TooManyRequests => new(HttpStatusCode.TooManyRequests, "Seerr rate limit reached.", "seerr_rate_limited"),
        _ => new(HttpStatusCode.BadGateway, "Seerr rejected the request.", "seerr_request_failed"),
    };

    private async Task<Connection> RequireConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = ToConnection(await LoadIntegrationAsync(cancellationToken));
        return connection ?? throw new IntegrationGatewayException(HttpStatusCode.Conflict, "Configure Seerr to continue.", "seerr_not_configured");
    }

    private Connection? ToConnection(Integration? integration)
    {
        var secret = integration?.Secrets.SingleOrDefault(s => s.SecretKey == ApiKeySecret);
        if (integration is null || !integration.Enabled || string.IsNullOrWhiteSpace(integration.BaseUrl) || secret is null)
            return null;
        try
        {
            return new Connection(
                integration.Id,
                integration.ConfigurationVersion,
                integration.BaseUrl.TrimEnd('/'),
                integration.OpenUrl?.TrimEnd('/'),
                _protector.Unprotect(secret.ProtectedValue));
        }
        catch (CryptographicException) { return null; }
    }

    private Task<Integration?> LoadIntegrationAsync(CancellationToken cancellationToken) =>
        _db.Integrations.Include(i => i.Secrets)
            .SingleOrDefaultAsync(i => i.Type == IntegrationType.Seerr && i.Name == IntegrationName, cancellationToken);

    // ── Mapping helpers ──────────────────────────────────────────────────────────────
    private static SeerrSearchResponseDto MapSearch(JsonElement root, string? publicUrl, int seerrUserId)
    {
        var page = root.TryGetProperty("page", out var p) && p.TryGetInt32(out var pv) ? pv : 1;
        var totalPages = root.TryGetProperty("totalPages", out var tp) && tp.TryGetInt32(out var tpv) ? tpv : 1;
        var totalResults = root.TryGetProperty("totalResults", out var tr) && tr.TryGetInt32(out var trv) ? trv : 0;
        var results = new List<SeerrMediaCardDto>();
        if (root.TryGetProperty("results", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var mediaType = GetString(item, "mediaType");
                if (mediaType is not ("movie" or "tv")) continue;
                var card = MapCard(item, mediaType, publicUrl, seerrUserId);
                if (card is not null) results.Add(card);
            }
        }
        return new SeerrSearchResponseDto(page, totalPages, totalResults, results);
    }

    private static SeerrMediaCardDto? MapCard(
        JsonElement item,
        string mediaType,
        string? publicUrl,
        int seerrUserId)
    {
        if (!item.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var tmdbId)) return null;
        var title = GetString(item, "title") ?? GetString(item, "name") ?? "Untitled";
        var date = GetString(item, "releaseDate") ?? GetString(item, "firstAirDate");
        var (mediaStatus, mediaStatus4k, requestStatus) = ReadMediaInfo(item, seerrUserId);
        return new SeerrMediaCardDto(
            mediaType,
            tmdbId,
            title,
            YearFromDate(date),
            ToImageUrl(publicUrl, GetString(item, "posterPath"), "w600_and_h900_bestv2"),
            ToImageUrl(publicUrl, GetString(item, "backdropPath"), "w1920_and_h800_multi_faces"),
            GetString(item, "overview"),
            GetDouble(item, "voteAverage"),
            mediaStatus,
            mediaStatus4k,
            requestStatus
        );
    }

    private static SeerrRequestDto MapRequest(JsonElement item, int currentSeerrUserId, string? publicUrl)
    {
        var id = item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var idv) ? idv : 0;
        var status = item.TryGetProperty("status", out var st) && st.TryGetInt32(out var stv) ? stv : 0;
        var is4k = item.TryGetProperty("is4k", out var f4) && f4.ValueKind == JsonValueKind.True;
        var mediaType = "movie";
        int tmdbId = 0, mediaStatus = 0;
        string? title = null, posterPath = null;
        if (item.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
        {
            mediaType = GetString(media, "mediaType") ?? "movie";
            tmdbId = media.TryGetProperty("tmdbId", out var tm) && tm.TryGetInt32(out var tmv) ? tmv : 0;
            var statusProperty = is4k ? "status4k" : "status";
            mediaStatus = media.TryGetProperty(statusProperty, out var ms) && ms.TryGetInt32(out var msv) ? msv : 0;
            title = GetString(media, "title") ?? GetString(media, "name");
            posterPath = ToImageUrl(publicUrl, GetString(media, "posterPath"), "w300_and_h450_face");
        }
        var seasons = new List<int>();
        if (item.TryGetProperty("seasons", out var se) && se.ValueKind == JsonValueKind.Array)
            seasons.AddRange(se.EnumerateArray()
                .Select(s => s.TryGetProperty("seasonNumber", out var sn) && sn.TryGetInt32(out var snv) ? snv : -1)
                .Where(n => n >= 0));
        string? requestedBy = null;
        int? requestedByUserId = null;
        if (item.TryGetProperty("requestedBy", out var rb) && rb.ValueKind == JsonValueKind.Object)
        {
            requestedBy = GetString(rb, "displayName") ?? GetString(rb, "username");
            requestedByUserId = rb.TryGetProperty("id", out var requestedById)
                && requestedById.TryGetInt32(out var parsedRequestedById)
                ? parsedRequestedById
                : null;
        }
        DateTime? createdAt = item.TryGetProperty("createdAt", out var ca) && ca.ValueKind == JsonValueKind.String
            && DateTime.TryParse(ca.GetString(), out var cav) ? cav.ToUniversalTime() : null;
        return new SeerrRequestDto(
            id,
            mediaType,
            tmdbId,
            title,
            posterPath,
            status,
            mediaStatus,
            is4k,
            requestedBy,
            requestedByUserId,
            requestedByUserId == currentSeerrUserId,
            seasons,
            createdAt);
    }

    private static Dictionary<int, (int? Status, int? Status4k)> ReadSeasonStatuses(JsonElement root)
    {
        var result = new Dictionary<int, (int? Status, int? Status4k)>();
        if (!root.TryGetProperty("mediaInfo", out var mediaInfo)
            || mediaInfo.ValueKind != JsonValueKind.Object
            || !mediaInfo.TryGetProperty("seasons", out var seasons)
            || seasons.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var season in seasons.EnumerateArray())
        {
            if (!season.TryGetProperty("seasonNumber", out var numberElement)
                || !numberElement.TryGetInt32(out var number))
                continue;
            int? status = season.TryGetProperty("status", out var statusElement)
                && statusElement.TryGetInt32(out var parsedStatus)
                ? parsedStatus
                : null;
            int? status4k = season.TryGetProperty("status4k", out var status4kElement)
                && status4kElement.TryGetInt32(out var parsedStatus4k)
                ? parsedStatus4k
                : null;
            result[number] = (status, status4k);
        }
        return result;
    }

    private static (int MediaStatus, int? MediaStatus4k, int? RequestStatus) ReadMediaInfo(
        JsonElement root,
        int seerrUserId)
    {
        if (!root.TryGetProperty("mediaInfo", out var info) || info.ValueKind != JsonValueKind.Object)
            return (1, null, null);
        var status = info.TryGetProperty("status", out var s) && s.TryGetInt32(out var sv) ? sv : 1;
        var status4k = info.TryGetProperty("status4k", out var s4) && s4.TryGetInt32(out var s4v) ? s4v : (int?)null;
        int? requestStatus = null;
        if (info.TryGetProperty("requests", out var reqs) && reqs.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reqs.EnumerateArray())
            {
                if (!r.TryGetProperty("requestedBy", out var requestedBy)
                    || requestedBy.ValueKind != JsonValueKind.Object
                    || !requestedBy.TryGetProperty("id", out var requestedById)
                    || !requestedById.TryGetInt32(out var parsedRequestedById)
                    || parsedRequestedById != seerrUserId)
                    continue;
                if (r.TryGetProperty("status", out var rs) && rs.TryGetInt32(out var rsv))
                {
                    requestStatus = rsv;
                    break;
                }
            }
        }
        return (status, status4k, requestStatus);
    }

    private static SeerrQuotaDto? ReadQuota(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var q) || q.ValueKind != JsonValueKind.Object) return null;
        var limit = q.TryGetProperty("limit", out var l) && l.TryGetInt32(out var lv) ? lv : (int?)null;
        var used = q.TryGetProperty("used", out var us) && us.TryGetInt32(out var uv) ? uv : 0;
        var remaining = q.TryGetProperty("remaining", out var rm) && rm.TryGetInt32(out var rmv) ? rmv : (int?)null;
        var days = q.TryGetProperty("days", out var dy) && dy.TryGetInt32(out var dyv) ? dyv : 0;
        var restricted = q.TryGetProperty("restricted", out var re) && re.ValueKind == JsonValueKind.True;
        return new SeerrQuotaDto(limit, used, remaining, days, restricted);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static string? YearFromDate(string? date) =>
        !string.IsNullOrWhiteSpace(date) && date.Length >= 4 ? date[..4] : null;

    private static int ClampPage(int page) => Math.Clamp(page, 1, 500);

    private static string NormalizeRequestFilter(string filter) => filter switch
    {
        "all" or "approved" or "available" or "pending" or "processing" or "unavailable" or "failed"
            or "completed" or "deleted" => filter,
        _ => "all",
    };

    private static SeerrUserMappingDto ToMappingDto(User user, UserPreference preference) => new(
        user.Id,
        user.UserName,
        preference.JellyfinUserId,
        preference.SeerrJellyfinMappingApproved,
        preference.SeerrUserIdOverride,
        preference.SeerrUserIdOverride is not null
            ? "override"
            : preference.SeerrJellyfinMappingApproved
                ? "jellyfin"
                : null);

    private static string NormalizeJellyfinUserId(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is < 1 or > 128
            || candidate.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Jellyfin user ID is invalid.");
        return candidate.ToLowerInvariant();
    }

    private static bool HasSameAuthority(string first, string second)
    {
        var firstUri = new Uri(first, UriKind.Absolute);
        var secondUri = new Uri(second, UriKind.Absolute);
        return string.Equals(firstUri.Scheme, secondUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(firstUri.Host, secondUri.Host, StringComparison.OrdinalIgnoreCase)
            && firstUri.Port == secondUri.Port;
    }

    private static string? ToImageUrl(string? publicUrl, string? path, string size)
    {
        if (string.IsNullOrWhiteSpace(path)
            || string.IsNullOrWhiteSpace(publicUrl)
            || !Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri))
            return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(absolute.UserInfo)
                || !string.IsNullOrEmpty(absolute.Query)
                || !string.IsNullOrEmpty(absolute.Fragment))
                return null;
            if (absolute.Host.Equals("image.tmdb.org", StringComparison.OrdinalIgnoreCase)
                && absolute.AbsolutePath.StartsWith("/t/p/", StringComparison.Ordinal))
                return $"{publicUrl.TrimEnd('/')}/imageproxy/tmdb{absolute.AbsolutePath}";
            if (HasSameAuthority(publicUri.ToString(), absolute.ToString())
                && absolute.AbsolutePath.StartsWith("/imageproxy/tmdb/", StringComparison.Ordinal))
                return absolute.ToString();
            return null;
        }
        if (!path.StartsWith('/')
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal)
            || path.Any(char.IsControl))
            return null;
        return $"{publicUrl.TrimEnd('/')}/imageproxy/tmdb/t/p/{size}{path}";
    }

    private static string NormalizeHttpUrl(string value)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Seerr URL must be an absolute HTTP(S) URL without credentials.");
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
    }

    private sealed record Connection(
        Guid IntegrationId,
        Guid ConfigurationVersion,
        string BaseUrl,
        string? PublicUrl,
        string ApiKey);
    private sealed record UserMapping(int SeerrUserId, string Source);
    private sealed record UserMapCacheEntry(string SourceKey, int SeerrUserId, DateTime ExpiresAt);
}
