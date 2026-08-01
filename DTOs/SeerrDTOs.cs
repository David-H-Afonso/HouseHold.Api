namespace Household.Api.DTOs;

// ── Admin configuration ────────────────────────────────────────────────────────
public sealed record SeerrConfigDto(
    bool Configured,
    string? InternalUrl,
    string? PublicUrl,
    bool HasApiKey,
    string? Version,
    bool Reachable
);

public sealed record UpdateSeerrConfigRequest(
    string InternalUrl,
    string PublicUrl,
    string? ApiKey
);

// ── Session / identity ─────────────────────────────────────────────────────────
public sealed record SeerrQuotaDto(int? Limit, int Used, int? Remaining, int Days, bool Restricted);

public sealed record SeerrSessionDto(
    bool Configured,
    bool Mapped,
    int? SeerrUserId,
    string? DisplayName,
    string? MappingSource,
    string? PublicUrl,
    int Permissions,
    bool CanRequestMovies,
    bool CanRequestTv,
    bool CanRequest4kMovies,
    bool CanRequest4kTv,
    bool CanManageRequests,
    bool CanViewAllRequests,
    SeerrQuotaDto? MovieQuota,
    SeerrQuotaDto? TvQuota
);

public sealed record UpdateSeerrUserMappingRequest(
    string Source,
    string? JellyfinUserId,
    int? SeerrUserId
);

public sealed record SeerrUserMappingDto(
    Guid HouseholdUserId,
    string UserName,
    string? JellyfinUserId,
    bool JellyfinMappingApproved,
    int? SeerrUserIdOverride,
    string? ActiveSource
);

// ── Media cards & search ───────────────────────────────────────────────────────
public sealed record SeerrMediaCardDto(
    string MediaType,
    int TmdbId,
    string Title,
    string? Year,
    string? PosterPath,
    string? BackdropPath,
    string? Overview,
    double? VoteAverage,
    int MediaStatus,
    int? MediaStatus4k,
    int? RequestStatus
);

public sealed record SeerrSearchResponseDto(
    int Page,
    int TotalPages,
    int TotalResults,
    IReadOnlyList<SeerrMediaCardDto> Results
);

// ── Detail ─────────────────────────────────────────────────────────────────────
public sealed record SeerrSeasonDto(
    int SeasonNumber,
    string? Name,
    int EpisodeCount,
    int? Status,
    int? Status4k
);

public sealed record SeerrDetailDto(
    string MediaType,
    int TmdbId,
    string Title,
    string? Year,
    string? PosterPath,
    string? BackdropPath,
    string? Overview,
    double? VoteAverage,
    int? Runtime,
    IReadOnlyList<string> Genres,
    IReadOnlyList<SeerrSeasonDto> Seasons,
    int MediaStatus,
    int? MediaStatus4k,
    int? RequestStatus,
    string? ImdbId,
    int? TvdbId
);

// ── Requests ───────────────────────────────────────────────────────────────────
public sealed record SeerrRequestDto(
    int Id,
    string MediaType,
    int TmdbId,
    string? Title,
    string? PosterPath,
    int RequestStatus,
    int MediaStatus,
    bool Is4k,
    string? RequestedBy,
    int? RequestedByUserId,
    bool IsMine,
    IReadOnlyList<int> Seasons,
    DateTime? CreatedAt
);

public sealed record SeerrRequestListDto(
    int Page,
    int TotalPages,
    int TotalResults,
    IReadOnlyList<SeerrRequestDto> Results
);

public sealed record CreateSeerrRequestBody(
    string MediaType,
    int MediaId,
    bool Is4k = false,
    IReadOnlyList<int>? Seasons = null
);
