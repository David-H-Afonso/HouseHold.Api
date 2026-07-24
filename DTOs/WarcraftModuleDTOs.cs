namespace Household.Api.DTOs;

public record WarcraftQuickStatusDto(
    int Total,
    int NotStarted,
    int Pending,
    int InProgress,
    int LastDay,
    int LastWeek,
    int Finished,
    DateTime GeneratedAtUtc
);

public record WarcraftWeeklySummaryDto(
    int Total,
    int NotStarted,
    int Pending,
    int InProgress,
    int LastDay,
    int LastWeek,
    int Finished,
    int Remaining,
    int CompletionPercent
);

public record WarcraftWeeklyItemDto(
    Guid Id,
    string CharacterName,
    string? CharacterClass,
    string ContentName,
    string Expansion,
    string Difficulty,
    string Status,
    DateTime? LastCompletedAt,
    DateTime UpdatedAt
);

public record WarcraftWeeklyDto(
    DateTime GeneratedAtUtc,
    WarcraftWeeklySummaryDto Summary,
    IReadOnlyList<WarcraftWeeklyItemDto> Items
);
