namespace Household.Api.DTOs;

public record JellywatchProfileDto(
    string DisplayName,
    int TotalSeriesWatching,
    int TotalSeriesCompleted,
    int TotalMoviesSeen,
    int TotalEpisodesSeen
);

public record JellywatchActivityDto(
    long EventId,
    string Title,
    string MediaType,
    string? EpisodeName,
    int? SeasonNumber,
    int? EpisodeNumber,
    string EventType,
    DateTime Timestamp,
    decimal? UserRating,
    string? OpenUrl
);

public record JellywatchUpcomingDto(
    long MediaItemId,
    long SeriesId,
    string SeriesTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string? EpisodeName,
    string AirDate,
    string? AirTime,
    string? AirTimeUtc,
    int BatchCount,
    string? PosterUrl,
    string? OpenUrl
);

public record JellywatchDashboardDto(
    JellywatchProfileDto Profile,
    IReadOnlyList<JellywatchActivityDto> Activity,
    IReadOnlyList<JellywatchUpcomingDto> Upcoming
);
