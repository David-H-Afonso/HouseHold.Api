using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IDoItClient
{
    Task<DoItNowDto> GetNowAsync(Guid userId, string? date, CancellationToken cancellationToken);
    Task<DoItOccurrenceActionDto> CompleteOccurrenceAsync(Guid userId, Guid occurrenceId, CancellationToken cancellationToken);
    Task<DoItOccurrenceActionDto> UndoOccurrenceAsync(Guid userId, Guid occurrenceId, CancellationToken cancellationToken);
}
