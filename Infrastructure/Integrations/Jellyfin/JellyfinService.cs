using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Household.Api.Application.Exceptions;
using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Infrastructure.Integrations.Jellyfin;

public sealed class JellyfinImageGrants
{
    private readonly ConcurrentDictionary<Guid, Grant> _grants = new();

    public void Set(Guid userId, IEnumerable<string> itemIds) =>
        _grants[userId] = new Grant(itemIds.ToHashSet(StringComparer.Ordinal), DateTime.UtcNow.AddMinutes(30));

    public bool Allows(Guid userId, string itemId) =>
        _grants.TryGetValue(userId, out var grant) && grant.ExpiresAt > DateTime.UtcNow && grant.ItemIds.Contains(itemId);

    private sealed record Grant(HashSet<string> ItemIds, DateTime ExpiresAt);
}

public sealed class JellyfinService : IJellyfinService
{
    private const int MaxJsonBytes = 1024 * 1024;
    private const int MaxImageBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private readonly AppDbContext _db;
    private readonly HttpClient _httpClient;
    private readonly IDataProtector _protector;
    private readonly JellyfinImageGrants _imageGrants;

    public JellyfinService(
        AppDbContext db,
        HttpClient httpClient,
        IDataProtectionProvider protectionProvider,
        JellyfinImageGrants imageGrants
    )
    {
        _db = db;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _protector = protectionProvider.CreateProtector("Household.Jellyfin.ApiKey.v1");
        _imageGrants = imageGrants;
    }

    public async Task<JellyfinConfigDto> GetConfigAsync(CancellationToken cancellationToken)
    {
        var integration = await LoadIntegrationAsync(cancellationToken);
        return ToConfigDto(integration);
    }

    public async Task<JellyfinConfigDto> UpdateConfigAsync(
        UpdateJellyfinConfigRequest request,
        CancellationToken cancellationToken
    )
    {
        var internalUrl = NormalizeHttpUrl(request.InternalUrl);
        var publicUrl = NormalizeHttpUrl(request.PublicUrl);
        var integration = await LoadIntegrationAsync(cancellationToken) ?? new Integration
        {
            Type = IntegrationType.Jellyfin,
            Name = "Jellyfin",
        };
        if (_db.Entry(integration).State == EntityState.Detached) _db.Integrations.Add(integration);
        integration.BaseUrl = internalUrl;
        integration.OpenUrl = publicUrl;
        integration.Enabled = true;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            if (request.ApiKey.Length > 1000) throw new ArgumentException("Jellyfin API key is invalid.");
            var secret = integration.Secrets.SingleOrDefault(item => item.SecretKey == "api-key");
            if (secret is null)
            {
                secret = new IntegrationSecret { SecretKey = "api-key" };
                integration.Secrets.Add(secret);
            }
            secret.ProtectedValue = _protector.Protect(request.ApiKey.Trim());
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ToConfigDto(integration);
    }

    public async Task<bool> ValidateUserAsync(string jellyfinUserId, CancellationToken cancellationToken)
    {
        var config = await GetConnectionAsync(cancellationToken);
        if (config is null) return false;
        using var response = await SendAsync(config, $"/Users/{Uri.EscapeDataString(jellyfinUserId)}", cancellationToken);
        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<JellyfinDashboardDto> GetDashboardAsync(Guid userId, CancellationToken cancellationToken)
    {
        var jellyfinUserId = await _db.UserPreferences.AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .Select(preference => preference.JellyfinUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(jellyfinUserId))
            throw new IntegrationGatewayException(HttpStatusCode.Conflict, "Map a Jellyfin user to continue.", "jellyfin_user_not_mapped");
        var config = await GetConnectionAsync(cancellationToken)
            ?? throw new IntegrationGatewayException(HttpStatusCode.Conflict, "Configure Jellyfin to continue.", "jellyfin_not_configured");

        var continueWatching = await GetItemsAsync(
            config,
            $"/Users/{Uri.EscapeDataString(jellyfinUserId)}/Items/Resume?Limit=24&Recursive=true&Fields=PrimaryImageAspectRatio,Overview",
            cancellationToken
        );
        var nextUp = await GetItemsAsync(
            config,
            $"/Shows/NextUp?UserId={Uri.EscapeDataString(jellyfinUserId)}&Limit=24&Fields=PrimaryImageAspectRatio,Overview",
            cancellationToken
        );
        var continueDtos = continueWatching.Select(item => ToDto(item, config.PublicUrl)).ToList();
        var nextDtos = nextUp.Select(item => ToDto(item, config.PublicUrl)).ToList();
        _imageGrants.Set(userId, continueDtos.Concat(nextDtos).Select(item => item.Id));
        var fallback = continueDtos.Count == 0;
        return new JellyfinDashboardDto(
            continueDtos,
            nextDtos,
            fallback ? nextDtos : continueDtos,
            fallback,
            config.PublicUrl
        );
    }

    public async Task<(byte[] Content, string ContentType)?> GetImageAsync(
        Guid userId,
        string itemId,
        CancellationToken cancellationToken
    )
    {
        if (!_imageGrants.Allows(userId, itemId)) return null;
        var config = await GetConnectionAsync(cancellationToken);
        if (config is null) return null;
        using var response = await SendAsync(config, $"/Items/{Uri.EscapeDataString(itemId)}/Images/Primary?maxWidth=500&quality=85", cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes) return null;
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!IsAllowedImageContentType(contentType)) return null;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > MaxImageBytes) return null;
            destination.Write(buffer, 0, read);
        }
        return (destination.ToArray(), contentType!);
    }

    private static bool IsAllowedImageContentType(string? contentType) => contentType is not null
        && contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
            || contentType?.Equals("image/png", StringComparison.OrdinalIgnoreCase) == true
            || contentType?.Equals("image/webp", StringComparison.OrdinalIgnoreCase) == true
            || contentType?.Equals("image/gif", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<List<SourceItem>> GetItemsAsync(
        Connection config,
        string path,
        CancellationToken cancellationToken
    )
    {
        using var response = await SendAsync(config, path, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Jellyfin is unavailable.", "jellyfin_unavailable");
        await response.Content.LoadIntoBufferAsync(MaxJsonBytes, cancellationToken);
        try
        {
            var result = await JsonSerializer.DeserializeAsync<SourceItems>(
                await response.Content.ReadAsStreamAsync(cancellationToken), JsonOptions, cancellationToken);
            return result?.Items ?? [];
        }
        catch (JsonException)
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Jellyfin returned an invalid response.", "invalid_provider_response");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Connection config, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.BaseUrl}{path}");
        request.Headers.Add("X-Emby-Token", config.ApiKey);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Household", "1.0"));
        try { return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new IntegrationGatewayException(HttpStatusCode.BadGateway, "Jellyfin is unavailable.", "jellyfin_unavailable");
        }
    }

    private async Task<Connection?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var integration = await LoadIntegrationAsync(cancellationToken);
        var secret = integration?.Secrets.SingleOrDefault(item => item.SecretKey == "api-key");
        if (integration is null || !integration.Enabled || string.IsNullOrWhiteSpace(integration.BaseUrl)
            || string.IsNullOrWhiteSpace(integration.OpenUrl) || secret is null) return null;
        try
        {
            return new Connection(integration.BaseUrl.TrimEnd('/'), integration.OpenUrl.TrimEnd('/'), _protector.Unprotect(secret.ProtectedValue));
        }
        catch (CryptographicException) { return null; }
    }

    private Task<Integration?> LoadIntegrationAsync(CancellationToken cancellationToken) =>
        _db.Integrations.Include(item => item.Secrets)
            .SingleOrDefaultAsync(item => item.Type == IntegrationType.Jellyfin && item.Name == "Jellyfin", cancellationToken);

    private static JellyfinConfigDto ToConfigDto(Integration? integration) => new(
        integration is { Enabled: true, BaseUrl: not null, OpenUrl: not null } && integration.Secrets.Any(item => item.SecretKey == "api-key"),
        integration?.OpenUrl,
        integration?.Secrets.Any(item => item.SecretKey == "api-key") == true
    );

    private static JellyfinItemDto ToDto(SourceItem item, string publicUrl)
    {
        int? progress = item.UserData?.PlaybackPositionTicks is > 0 && item.RunTimeTicks is > 0
            ? (int)Math.Clamp(Math.Round(item.UserData.PlaybackPositionTicks.Value * 100d / item.RunTimeTicks.Value), 0, 100)
            : null;
        return new JellyfinItemDto(
            NormalizeProviderText(item.Id, 128),
            NormalizeProviderText(item.Name, 300),
            NormalizeOptionalProviderText(item.SeriesName, 300),
            item.ParentIndexNumber,
            item.IndexNumber,
            item.RunTimeTicks,
            item.UserData?.PlaybackPositionTicks,
            progress,
            $"/api/v1/jellyfin/images/{Uri.EscapeDataString(item.Id)}",
            BuildItemOpenUrl(publicUrl, item.Id)
        );
    }

    private static string NormalizeProviderText(string? value, int maxLength)
    {
        var normalized = new string((value ?? string.Empty).Where(character => !char.IsControl(character)).ToArray()).Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static string? NormalizeOptionalProviderText(string? value, int maxLength)
    {
        var normalized = NormalizeProviderText(value, maxLength);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string BuildItemOpenUrl(string publicUrl, string itemId)
    {
        if (!Uri.TryCreate(publicUrl.TrimEnd('/') + "/", UriKind.Absolute, out var origin)
            || !Uri.TryCreate(origin, $"web/#/details?id={Uri.EscapeDataString(itemId)}", out var itemUrl)
            || !string.Equals(origin.Scheme, itemUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Host, itemUrl.Host, StringComparison.OrdinalIgnoreCase)
            || origin.Port != itemUrl.Port)
            throw new InvalidOperationException("Jellyfin public URL is invalid.");
        return itemUrl.ToString();
    }

    private static string NormalizeHttpUrl(string value)
    {
        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Jellyfin URL must be an absolute HTTP(S) URL without credentials.");
        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
    }

    private sealed record Connection(string BaseUrl, string PublicUrl, string ApiKey);
    private sealed class SourceItems { public List<SourceItem> Items { get; set; } = []; }
    private sealed class SourceItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? SeriesName { get; set; }
        public int? ParentIndexNumber { get; set; }
        public int? IndexNumber { get; set; }
        public long? RunTimeTicks { get; set; }
        public SourceUserData? UserData { get; set; }
    }
    private sealed class SourceUserData { public long? PlaybackPositionTicks { get; set; } }
}
