using Household.Api.Models.Integrations;

namespace Household.Api.DTOs;

public sealed record CasaOsUpdateConfigDto(bool Configured, bool HasToken, bool HasRefreshToken);

public sealed record UpdateCasaOsUpdateConfigRequest(string InternalBaseUrl, string? RawToken, string? RawRefreshToken = null);

public sealed record CasaOsRollbackRequest(string Confirmation, string? BackupId);

public sealed record CasaOsQueuedOperationDto(
    Guid ActionLogId,
    string AppId,
    string Action,
    IntegrationActionStatus Status,
    string Message,
    DateTime StartedAt,
    string BackupId,
    string? SafetyBackupId
);

public sealed record CasaOsActionStatusDto(
    Guid ActionLogId,
    string AppId,
    string Action,
    IntegrationActionStatus Status,
    string Message,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? BackupId,
    string? SafetyBackupId,
    IReadOnlyList<string> PreviousImages,
    string? ErrorCode,
    bool RollbackAvailable
);

public sealed record CasaOsAppCapabilities(
    bool Configured,
    IReadOnlyDictionary<string, bool?> UpdateAvailability
);
