namespace Household.Api.DTOs;

public record GameModuleItemDto(
    int Id,
    string Name,
    int StatusId,
    string? StatusName,
    string? PlatformName,
    string? Logo,
    string? Cover,
    int? Grade,
    decimal? Score,
    string? Started,
    string? Finished,
    int? SteamAppId,
    int? SteamPlaytimeForever,
    bool Favorite,
    string? Released,
    string? Comment,
    int? Critic,
    string? CriticProvider,
    int? Story,
    int? Completion,
    string? PlayedStatusName,
    IReadOnlyList<string> PlayWithNames,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string? OpenUrl
);

public record GamesModuleListDto(
    IReadOnlyList<GameModuleItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

public record GameStatusOptionDto(int Id, string Name, string Color, string StatusType);

public record UpdateGameStatusRequest(int StatusId);

public record GamesSummaryDto(
    int TotalCount,
    IReadOnlyList<GameStatusOptionDto> Statuses,
    IReadOnlyDictionary<string, int> CountsByStatus
);
