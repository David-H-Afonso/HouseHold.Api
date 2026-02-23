using Household.Api.Models.Home;

namespace Household.Api.DTOs;

// ── Room ──────────────────────────────────────────────────────────────────────

public record CreateRoomRequest(string Name);

public record UpdateRoomRequest(string Name);

public record RoomDto(Guid Id, string Name, DateTime CreatedAt);

// ── TaskTemplate ──────────────────────────────────────────────────────────────

public record CreateTaskTemplateRequest(
    string Title,
    Guid? RoomId,
    string? Description,
    Guid? AssignedToUserId,
    ScheduleType ScheduleType,
    TimeOfDaySlot TimeOfDaySlot,
    int? DaysOfWeekMask,
    int? DayOfMonth,
    int? IntervalDays,
    DateOnly StartDate,
    bool CarryOverIfMissed = false,
    bool IsActive = true
);

public record UpdateTaskTemplateRequest(
    string Title,
    Guid? RoomId,
    string? Description,
    Guid? AssignedToUserId,
    ScheduleType ScheduleType,
    TimeOfDaySlot TimeOfDaySlot,
    int? DaysOfWeekMask,
    int? DayOfMonth,
    int? IntervalDays,
    DateOnly StartDate,
    bool CarryOverIfMissed,
    bool IsActive
);

public record TaskTemplateDto(
    Guid Id,
    string Title,
    Guid? RoomId,
    string? RoomName,
    string? Description,
    Guid? AssignedToUserId,
    string? AssignedToUserName,
    bool IsActive,
    ScheduleType ScheduleType,
    TimeOfDaySlot TimeOfDaySlot,
    int? DaysOfWeekMask,
    int? DayOfMonth,
    int? IntervalDays,
    DateOnly StartDate,
    bool CarryOverIfMissed,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

// ── TaskInstance ──────────────────────────────────────────────────────────────

public record CompleteTaskInstanceRequest(string? Notes = null);

public record TaskInstanceDto(
    Guid Id,
    Guid TaskTemplateId,
    string TaskTitle,
    string? RoomName,
    DateOnly DueDate,
    TimeOfDaySlot TimeOfDaySlot,
    Guid? AssignedToUserId,
    string? AssignedToUserName,
    TaskInstanceStatus Status,
    DateTime? CompletedAt,
    Guid? CompletedByUserId,
    string? Notes,
    bool IsOverdue
);

public record TodayTasksResponse(
    DateOnly Date,
    List<TaskInstanceDto> Morning,
    List<TaskInstanceDto> Night,
    List<TaskInstanceDto> Anytime,
    List<TaskInstanceDto> Overdue
);

// ── HomeIssue ─────────────────────────────────────────────────────────────────

public record CreateHomeIssueRequest(
    string Title,
    Guid? RoomId,
    string? Description,
    IssueStatus Status = IssueStatus.Open,
    int Priority = 0
);

public record UpdateHomeIssueRequest(string Title, Guid? RoomId, string? Description, IssueStatus Status, int Priority);

public record HomeIssueDto(
    Guid Id,
    string Title,
    Guid? RoomId,
    string? RoomName,
    string? Description,
    IssueStatus Status,
    int Priority,
    Guid CreatedByUserId,
    string CreatedByUserName,
    DateTime CreatedAt,
    DateTime? ResolvedAt
);
