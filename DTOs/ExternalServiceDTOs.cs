namespace Household.Api.DTOs;

public sealed record JellyfinConfigDto(bool Configured, string? PublicUrl, bool HasApiKey);
public sealed record UpdateJellyfinConfigRequest(string InternalUrl, string PublicUrl, string? ApiKey);
public sealed record JellyfinItemDto(
    string Id,
    string Name,
    string? SeriesName,
    int? ParentIndexNumber,
    int? IndexNumber,
    long? RunTimeTicks,
    long? PlaybackPositionTicks,
    int? ProgressPercent,
    string ImageUrl,
    string OpenUrl
);
public sealed record JellyfinDashboardDto(
    IReadOnlyList<JellyfinItemDto> ContinueWatching,
    IReadOnlyList<JellyfinItemDto> NextUp,
    IReadOnlyList<JellyfinItemDto> DashboardItems,
    bool UsedNextUpFallback,
    string? OpenUrl
);

public sealed record GitHubActionsConfigDto(bool Configured, bool HasToken);
public sealed record UpdateGitHubActionsConfigRequest(string? Token);
public sealed record GitHubWorkflowRunDto(
    string Repository,
    long? RunId,
    string? WorkflowName,
    string? Status,
    string? Conclusion,
    string? Branch,
    string? Commit,
    string? Actor,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    long? DurationSeconds,
    string? Url,
    DateTime? LastSuccessfulPoll,
    bool Degraded,
    string? ErrorCode
);
public sealed record GitHubActionsMonitorDto(
    DateTime GeneratedAtUtc,
    DateTime? LastSuccessfulPoll,
    bool Degraded,
    IReadOnlyList<GitHubWorkflowRunDto> Repositories
);
