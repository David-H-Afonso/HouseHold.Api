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
    string? RecommendedTime,
    string AssignmentMode,
    IReadOnlyList<Guid> AssigneeIds,
    IReadOnlyList<string> AssigneeNames,
    string TimeZoneId,
    string RecurrenceType,
    DateTime? CompletedAt,
    Guid? CompletedByUserId
);

public record DoItNowDto(
    string Date,
    string Scope,
    DoItProgressDto Progress,
    IReadOnlyList<DoItNowTaskDto> Tasks
);

public record DoItOccurrenceActionDto(
    Guid OccurrenceId,
    Guid TaskId,
    string OccurrenceDate,
    string OccurrenceStatus
);
