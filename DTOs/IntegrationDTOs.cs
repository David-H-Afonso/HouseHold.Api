using Household.Api.Models.Integrations;

namespace Household.Api.DTOs;

public record IntegrationResponse(
    Guid Id,
    IntegrationType Type,
    string Name,
    string? OpenUrl,
    bool Enabled,
    IntegrationHealthStatus LastHealthStatus,
    DateTime? LastCheckedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<string> SecretKeys
);

public record UpsertIntegrationRequest(
    IntegrationType Type,
    string Name,
    string? BaseUrl,
    string? OpenUrl,
    bool Enabled,
    Dictionary<string, string>? Secrets
);

public record IntegrationHealthDto(
    Guid? IntegrationId,
    IntegrationType Type,
    string Name,
    IntegrationHealthStatus Status,
    string Message,
    DateTime CheckedAt
);

public record DashboardWidgetDto(
    Guid Id,
    string WidgetType,
    Guid? IntegrationId,
    int Position,
    bool Enabled,
    string? SettingsJson
);

public record DashboardResponse(
    DateTime GeneratedAt,
    IReadOnlyList<IntegrationHealthDto> IntegrationHealth,
    IReadOnlyList<DashboardWidgetDto> Widgets
);

public record IntegrationActionLogDto(
    Guid Id,
    Guid? UserId,
    Guid? IntegrationId,
    string? AppId,
    string Action,
    IntegrationActionStatus Status,
    string Source,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? ErrorMessage
);
