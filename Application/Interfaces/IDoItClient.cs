using Household.Api.DTOs;

namespace Household.Api.Application.Interfaces;

public interface IDoItClient
{
    Task<DoItNowDto> GetNowAsync(Guid userId, string date, string timeZoneId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DoItCalendarEventDto>> GetCalendarEventsAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<DoItOccurrenceActionDto> CompleteOccurrenceAsync(
        Guid userId,
        Guid occurrenceId,
        string? occurrenceDate,
        string timeZoneId,
        CancellationToken cancellationToken);
    Task<DoItOccurrenceActionDto> UndoOccurrenceAsync(
        Guid userId,
        Guid occurrenceId,
        string? occurrenceDate,
        string timeZoneId,
        CancellationToken cancellationToken);
}
