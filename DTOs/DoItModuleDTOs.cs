namespace Household.Api.DTOs;

public record DoItProgressDto(int Total, int Done, int Missed, int NotApplicable, int Pending);

public record DoItNowTaskDto(
    Guid OccurrenceId,
    Guid Id,
    string Title,
    string? ZoneName,
    string Scope,
    string State,
    string OccurrenceStatus,
    string OccurrenceDate,
    string? AvailableFromTime,
    string? AvailableUntilTime,
    string? RecommendedTime
);

public record DoItNowDto(
    string Date,
    string Scope,
    DoItProgressDto Progress,
    IReadOnlyList<DoItNowTaskDto> Tasks
);
