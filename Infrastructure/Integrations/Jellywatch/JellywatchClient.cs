using Household.Api.Application.Interfaces;
using Household.Api.Configuration;
using Household.Api.DTOs;
using Microsoft.Extensions.Options;

namespace Household.Api.Infrastructure.Integrations.Jellywatch;

public sealed class JellywatchClient : HouseholdProviderClientBase, IJellywatchClient
{
    private readonly HouseholdConnectionSettings _settings;

    public JellywatchClient(
        HttpClient httpClient,
        IHouseholdProviderAccessService connectionAccess,
        IOptions<HouseholdConnectionSettings> settings
    ) : base(httpClient, connectionAccess, "jellywatch", "Jellywatch")
    {
        _settings = settings.Value;
    }

    public async Task<JellywatchDashboardDto> GetDashboardAsync(Guid userId, string timeZoneId, CancellationToken cancellationToken)
    {
        var source = await GetRequiredAsync<SourceDashboard>(
            userId,
            "activity.read",
            $"/api/integrations/household/v1/dashboard?activityLimit=20&upcomingDays=8&timeZoneId={Uri.EscapeDataString(timeZoneId)}",
            cancellationToken
        );

        return new JellywatchDashboardDto(
            new JellywatchProfileDto(
                source.Profile.DisplayName,
                source.Profile.TotalSeriesWatching,
                source.Profile.TotalSeriesCompleted,
                source.Profile.TotalMoviesSeen,
                source.Profile.TotalEpisodesSeen
            ),
            source.Activity.Select(item => new JellywatchActivityDto(
                item.EventId,
                item.Title,
                item.MediaType,
                item.EpisodeName,
                item.SeasonNumber,
                item.EpisodeNumber,
                item.EventType,
                item.Timestamp,
                $"/modules/media/jellywatch/posters/{item.MediaItemId}?source=activity",
                item.UserRating,
                item.TmdbRating,
                BuildPublicUrl(_settings.JellywatchOpenUrl, "/#/activity")
            )).ToList(),
            FilterUpcoming(source.Upcoming, timeZoneId).Select(item => new JellywatchUpcomingDto(
                item.MediaItemId,
                item.SeriesId,
                item.SeriesTitle,
                item.SeasonNumber,
                item.EpisodeNumber,
                item.EpisodeName,
                item.AirDate,
                item.AirTime,
                item.AirTimeUtc,
                item.BatchCount,
                $"/modules/media/jellywatch/posters/{item.MediaItemId}?source=upcoming",
                BuildPublicUrl(_settings.JellywatchOpenUrl, $"/#/series/{item.SeriesId}")
            )).ToList()
        );
    }

    public async Task<(byte[] Content, string ContentType)?> GetPosterAsync(
        Guid userId,
        long mediaItemId,
        string requiredScope,
        CancellationToken cancellationToken
    )
    {
        if (mediaItemId <= 0) return null;
        var file = await DownloadAsync(userId, requiredScope, $"/api/asset/{mediaItemId}/poster", 8 * 1024 * 1024, cancellationToken);
        return file is null || !IsAllowedImageContentType(file.ContentType)
            ? null
            : (file.Content, file.ContentType);
    }

    private static bool IsAllowedImageContentType(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<SourceUpcoming> FilterUpcoming(IReadOnlyList<SourceUpcoming> items, string timeZoneId)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch { zone = TimeZoneInfo.Utc; }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var end = today.AddDays(7);
        return items.Where(item => DateOnly.TryParse(item.AirDate, out var date) && date >= today && date < end).ToList();
    }

    private sealed class SourceDashboard
    {
        public SourceProfile Profile { get; set; } = new();
        public List<SourceActivity> Activity { get; set; } = [];
        public List<SourceUpcoming> Upcoming { get; set; } = [];
    }

    private sealed class SourceProfile
    {
        public string DisplayName { get; set; } = string.Empty;
        public int TotalSeriesWatching { get; set; }
        public int TotalSeriesCompleted { get; set; }
        public int TotalMoviesSeen { get; set; }
        public int TotalEpisodesSeen { get; set; }
    }

    private sealed class SourceActivity
    {
        public long EventId { get; set; }
        public long MediaItemId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string? EpisodeName { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? PosterUrl { get; set; }
        public decimal? UserRating { get; set; }
        public double? TmdbRating { get; set; }
    }

    private sealed class SourceUpcoming
    {
        public long MediaItemId { get; set; }
        public long SeriesId { get; set; }
        public string SeriesTitle { get; set; } = string.Empty;
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public string? EpisodeName { get; set; }
        public string AirDate { get; set; } = string.Empty;
        public string? AirTime { get; set; }
        public string? AirTimeUtc { get; set; }
        public int BatchCount { get; set; } = 1;
        public string? PosterUrl { get; set; }
    }
}
