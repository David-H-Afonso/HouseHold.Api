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
