using Household.Api.DTOs;
using Household.Api.Models.Integrations;

namespace Household.Api.Application.Interfaces;

public interface IIntegrationActionLogService
{
    Task<IntegrationActionLogDto> LogAsync(
        Guid? userId,
        Guid? integrationId,
        string? appId,
        string action,
        IntegrationActionStatus status,
        string? requestSummaryJson,
        string? resultSummaryJson,
        string? errorMessage,
        CancellationToken cancellationToken
    );
}
