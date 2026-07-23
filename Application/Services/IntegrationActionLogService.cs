using Household.Api.Application.Interfaces;
using Household.Api.Data;
using Household.Api.DTOs;
using Household.Api.Models.Integrations;

namespace Household.Api.Application.Services;

public class IntegrationActionLogService : IIntegrationActionLogService
{
    private readonly AppDbContext _db;

    public IntegrationActionLogService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IntegrationActionLogDto> LogAsync(
        Guid? userId,
        Guid? integrationId,
        string? appId,
        string action,
        IntegrationActionStatus status,
        string? requestSummaryJson,
        string? resultSummaryJson,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        var log = new IntegrationActionLog
        {
            UserId = userId,
            IntegrationId = integrationId,
            AppId = appId,
            Action = action,
            Status = status,
            RequestSummaryJson = requestSummaryJson,
            ResultSummaryJson = resultSummaryJson,
            ErrorMessage = errorMessage,
            StartedAt = now,
            FinishedAt = status is IntegrationActionStatus.Succeeded or IntegrationActionStatus.Failed ? now : null,
        };

        _db.IntegrationActionLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        return new IntegrationActionLogDto(
            log.Id,
            log.UserId,
            log.IntegrationId,
            log.AppId,
            log.Action,
            log.Status,
            log.Source,
            log.StartedAt,
            log.FinishedAt,
            log.ErrorMessage
        );
    }
}
